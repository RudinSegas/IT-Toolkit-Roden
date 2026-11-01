using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Toolkit.Core;

namespace Toolkit.Privilege
{
    public sealed class PrivilegeService : IPrivilegeProbe
    {
        private readonly ILogger logger;
        public PrivilegeService(ILogger log) { logger = log; }
        public async Task<string> RunAsync(CancellationToken token)
        {
            var result = await ExecuteAsync("WorkerProbe", null, token).ConfigureAwait(false);
            return result.ExitCode == 0 ? "Verified: authenticated helper has an elevated administrator token. No system settings were changed." : "Worker probe failed.";
        }
        public async Task<ProcessResult> ExecuteAsync(string operation, Action<OutputChunk> output, CancellationToken token)
        {
            if (!Operations.IsElevated(operation)) throw new ArgumentException("Unknown privileged operation.");
            string session = Guid.NewGuid().ToString("N"), request = Guid.NewGuid().ToString("N"), nonce;
            using (var random = RandomNumberGenerator.Create()) { var bytes = new byte[32]; random.GetBytes(bytes); nonce = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
            using (var parent = Process.GetCurrentProcess())
            using (var lease = new ImageLease(NativeSecurity.ImagePath))
            using (var server = CreatePipe(session))
            {
                logger.Write("ElevationRequest", operation, "Pending", correlation: request);
                Process helper = null;
                try
                {
                    var connected = Task.Factory.FromAsync(server.BeginWaitForConnection, server.EndWaitForConnection, null);
                    // RunAs can block while the secure desktop is open; never block the WPF dispatcher.
                    helper = await Task.Run(() => Process.Start(new ProcessStartInfo(NativeSecurity.ImagePath,
                        Arguments.Join(new[] { "--worker", session, parent.Id.ToString(), parent.StartTime.ToUniversalTime().Ticks.ToString(), nonce }))
                        { UseShellExecute = true, Verb = "runas", WorkingDirectory = Environment.SystemDirectory })).ConfigureAwait(false);
                    if (helper == null) throw new IOException("Windows did not return a worker process handle.");
                    long started = helper.StartTime.ToUniversalTime().Ticks;
                    if (await Task.WhenAny(connected, Task.Delay(30000, token)).ConfigureAwait(false) != connected)
                        throw new TimeoutException("Elevated helper did not connect within 30 seconds. Check application-control and Windows logs. No operation was authorized.");
                    await connected.ConfigureAwait(false);
                    uint pid; if (!NativeSecurity.GetNamedPipeClientProcessId(server.SafePipeHandle, out pid) || pid != helper.Id) throw new InvalidDataException("Unexpected pipe client process.");
                    NativeSecurity.VerifyPeer(helper, NativeSecurity.ImagePath, started, parent.SessionId, true);
                    var approval = NewMessage("Execute", operation, session, request, nonce);
                    await ProbeProtocol.WriteAsync(server, approval, token).ConfigureAwait(false);
                    logger.Write("WorkerAuthenticated", operation, "Verified", correlation: request);
                    var clock = Stopwatch.StartNew();
                    long lastSequence = 0; bool cancelSent = false;
                    while (true)
                    {
                        if (token.IsCancellationRequested && !cancelSent) { cancelSent = true; await ProbeProtocol.WriteAsync(server, NewMessage("Cancel", operation, session, request, nonce), CancellationToken.None).ConfigureAwait(false); }
                        // Cancellation is advisory during servicing: do not disconnect or kill the worker.
                        var read = ProbeProtocol.ReadAsync(server, CancellationToken.None);
                        while (await Task.WhenAny(read, Task.Delay(500)).ConfigureAwait(false) != read)
                        {
                            if (helper.HasExited) throw new IOException("Helper exited without a complete result. Review its diagnostic log.");
                            if (clock.Elapsed.TotalHours > 8) throw new TimeoutException("Monitoring exceeded eight hours. The active command was not forcibly stopped; inspect Windows servicing before retrying.");
                        }
                        var message = await read.ConfigureAwait(false);
                        message.Validate(message.Type, session, request, nonce);
                        if (message.Sequence != ++lastSequence) throw new InvalidDataException("Worker message sequence mismatch.");
                        if (message.Operation != operation || message.ProcessId != helper.Id || !message.Elevated) throw new InvalidDataException("Worker response binding failed.");
                        if (message.Type == "Complete")
                        {
                            if (!message.ExitCode.HasValue) throw new InvalidDataException("Missing process exit code.");
                            logger.Write("WorkerComplete", operation, message.Verdict ?? (message.ExitCode == 0 ? "ProcessCompleted" : "Failed"), "Worker result code: " + message.ExitCode + "; native exit: " + message.NativeExitCode, clock.Elapsed.TotalMilliseconds, request);
                            if(message.ExitCode.Value==1223)throw new OperationCanceledException("Worker cancelled at a safe checkpoint.");
                            return new ProcessResult { Summary=message.Text, Verdict=message.Verdict, NativeExitCode=message.NativeExitCode, ExitCode = message.ExitCode.Value, DurationMs = clock.Elapsed.TotalMilliseconds, CancelPending = token.IsCancellationRequested };
                        }
                        if (message.Type != "Output" && message.Type != "Heartbeat") throw new InvalidDataException("Unexpected worker event sequence.");
                        if (message.Type == "Output" && output != null) output(new OutputChunk { Channel = message.Channel ?? "status", Text = message.Text ?? "", MeasuredPercent = message.Percent });
                    }
                }
                catch (Win32Exception e)
                { if (e.NativeErrorCode == 1223) { logger.Write("ElevationCancelled", operation, "Cancelled"); throw new OperationCanceledException("UAC was cancelled. No operation was started."); } throw; }
                finally { if (helper != null) helper.Dispose(); }
            }
        }
        internal static ProbeMessage NewMessage(string type, string operation, string session, string request, string nonce)
        { return new ProbeMessage { Version = 1, Type = type, Operation = operation, Session = session, Request = request, Nonce = nonce, ProcessId = Process.GetCurrentProcess().Id }; }

        [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint instances, uint output, uint input, uint timeout, ref SecurityAttributes security);
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafePipeHandle OpenPipe(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        private static NamedPipeClientStream ConnectClient(string session)
        {
            // Request only data/attribute/synchronization rights. GenericWrite would also request CreateNewInstance.
            var handle = OpenPipe(@"\\.\pipe\ITAdminToolkit-" + session, 0x100083, 0, IntPtr.Zero, 3, 0x40110000, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot connect to the authenticated GUI pipe."); }
            try { return new NamedPipeClientStream(PipeDirection.InOut, true, true, handle); }
            catch { handle.Dispose(); throw; }
        }
        private static NamedPipeServerStream CreatePipe(string session)
        {
            var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
            // Client data rights exclude CreateNewInstance (bit 4). Only the creating user owns the server.
            var dataRights = PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.Synchronize | PipeAccessRights.ReadAttributes;
            using (var id = WindowsIdentity.GetCurrent())
            {
                security.SetOwner(id.User);
                security.AddAccessRule(new PipeAccessRule(id.User, dataRights, AccessControlType.Allow));
            }
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), dataRights, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            var descriptor = security.GetSecurityDescriptorBinaryForm(); var pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
            try
            {
                var attributes = new SecurityAttributes { Length = Marshal.SizeOf(typeof(SecurityAttributes)), Descriptor = pinned.AddrOfPinnedObject(), Inherit = false };
                // DUPLEX | OVERLAPPED | FIRST_PIPE_INSTANCE, byte mode, remote clients rejected.
                var handle = CreateNamedPipe(@"\\.\pipe\ITAdminToolkit-" + session, 3 | 0x40000000 | 0x80000, 8, 1, 16384, 16384, 0, ref attributes);
                if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
                try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
                catch { handle.Dispose(); throw; }
            }
            finally { pinned.Free(); }
        }
        public static int WorkerMain(string[] args)
        {
            // Only this exact bootstrap syntax is accepted; operation and arguments arrive over the authenticated channel.
            if (args.Length != 5 || args[0] != "--worker" || !Regex.IsMatch(args[1], "\\A[0-9a-f]{32}\\z") || !Regex.IsMatch(args[4], "\\A[0-9a-f]{64}\\z")) return 64;
            int parentId; long start;
            if (!Int32.TryParse(args[2], out parentId) || parentId <= 0 || !Int64.TryParse(args[3], out start) || start <= 0) return 64;
            ILogger log = new UnavailableLogger();
            try
            {
                try { log = new JsonLogger(NativeSecurity.CreatePrivateLogDirectory()); } catch { }
                log.Write("WorkerStartup", "Bootstrap", "Started");
                return WorkerAsync(args[1], parentId, start, args[4], log).GetAwaiter().GetResult();
            }
            catch (Exception e) { log.Write("WorkerFailure", "Bootstrap", "Failed", e.GetType().Name + ": " + e.Message); return 1; }
            finally { log.Dispose(); }
        }
        private static async Task<int> WorkerAsync(string session, int parentId, long start, string nonce, ILogger log)
        {
            using (var self = Process.GetCurrentProcess())
            using (var parent = Process.GetProcessById(parentId))
            using (var lease = new ImageLease(NativeSecurity.ImagePath))
            using (var channel = ConnectClient(session))
            {
                if (!NativeSecurity.IsElevated(self)) throw new UnauthorizedAccessException("Worker requires an administrator token.");
                NativeSecurity.VerifyPeer(parent, NativeSecurity.ImagePath, start, self.SessionId, false);
                uint server; if (!NativeSecurity.GetNamedPipeServerProcessId(channel.SafePipeHandle, out server) || server != parentId) throw new InvalidDataException("Unexpected pipe server.");
                NativeSecurity.VerifyPeer(parent, NativeSecurity.ImagePath, start, self.SessionId, false);
                ProbeMessage approved;
                using (var deadline = new CancellationTokenSource(30000))
                using (deadline.Token.Register(() => channel.Dispose()))
                {
                    var request = await ProbeProtocol.ReadAsync(channel, deadline.Token).ConfigureAwait(false);
                    request.Validate("Execute", session, request.Request, nonce);
                    if (!Operations.IsElevated(request.Operation) || request.ProcessId != parentId) throw new InvalidDataException("Rejected privileged request.");
                    // Admission completed; no arbitrary path, arguments, code or download location crosses the boundary.
                    approved = request;
                }
                return await RunAuthorized(channel, approved, log).ConfigureAwait(false);
            }
        }
        private static async Task<int> RunAuthorized(NamedPipeClientStream channel, ProbeMessage request, ILogger log)
        {
            using (var operationStop = new CancellationTokenSource())
            {
            Task cancelReader = Task.Run(async () => {
                try { var cancel = await ProbeProtocol.ReadAsync(channel, CancellationToken.None).ConfigureAwait(false); cancel.Validate("Cancel",request.Session,request.Request,request.Nonce); if(cancel.Operation!=request.Operation || cancel.ProcessId!=request.ProcessId)throw new InvalidDataException("Cancel identity mismatch."); operationStop.Cancel(); }
                catch (IOException) { } catch (ObjectDisposedException) { } catch (Exception e) { log.Write("CancelRejected",request.Operation,"Rejected",e.Message); }
            });
            var gate = new SemaphoreSlim(1, 1); bool disconnected = false; long sequence = 0;
            Func<ProbeMessage, Task> send = async message =>
            {
                message.Elevated = true;
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    message.Sequence = ++sequence;
                    if (!disconnected)
                    {
                        var write = ProbeProtocol.WriteAsync(channel, message, CancellationToken.None);
                        if (await Task.WhenAny(write, Task.Delay(2000)).ConfigureAwait(false) != write) { disconnected = true; channel.Dispose(); }
                        await write.ConfigureAwait(false);
                    }
                }
                catch (IOException) { disconnected = true; } catch (ObjectDisposedException) { disconnected = true; }
                finally { gate.Release(); }
            };
            var result = new ProcessResult { ExitCode = 0 };
            if (request.Operation != "WorkerProbe")
            {
                // An admin-owned fixed coordination file prevents concurrent servicing across application instances.
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IT-Admin-Toolkit-Servicing");
                var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null); acl.SetOwner(admins);
                foreach (var sid in new[] { admins, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) }) acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                var dir = new DirectoryInfo(root); dir.Create(acl);
                if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Servicing directory is a reparse point.");
                var existing = dir.GetAccessControl();
                if (!existing.GetOwner(typeof(SecurityIdentifier)).Equals(admins)) throw new IOException("Servicing directory has an untrusted owner.");
                // Reapply a protected admin-only ACL before opening coordination data; never execute files here.
                dir.SetAccessControl(acl);
                string lockPath = Path.Combine(root, "servicing.lock");
                if (File.Exists(lockPath) && (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0) throw new IOException("Servicing lock is a reparse point.");
                using (var servicing = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    string progressTail = ""; var progressGate = new object();
                    Action<OutputChunk> sink = chunk =>
                    {
                        log.Write("CommandOutput", request.Operation, "Running", chunk.Text, correlation: request.Request);
                        var m = NewMessage("Output", request.Operation, request.Session, request.Request, request.Nonce); m.Text = chunk.Text; m.Channel = chunk.Channel;
                        lock (progressGate) { if (chunk.MeasuredPercent.HasValue) m.Percent = chunk.MeasuredPercent; if (chunk.Channel == "stdout" && !request.Operation.StartsWith("Install:") && !request.Operation.StartsWith("Upgrade:")) { progressTail = (progressTail + chunk.Text); if (progressTail.Length > 2048) progressTail = progressTail.Substring(progressTail.Length - 2048); m.Percent = ProgressParser.Parse(progressTail); } }
                        // Best-effort IPC has a bounded writer wait; native servicing continues if the GUI stops reading.
                        var sending = send(m); if (!sending.Wait(2000)) { disconnected = true; channel.Dispose(); }
                    };
                    log.Write("ProcessStart", request.Operation, "Started", correlation: request.Request);
                    using (var beatStop = new CancellationTokenSource())
                    {
                        var beats = Task.Run(async () => { while (!beatStop.IsCancellationRequested) { try { await Task.Delay(3000, beatStop.Token).ConfigureAwait(false); await send(NewMessage("Heartbeat", request.Operation, request.Session, request.Request, request.Nonce)).ConfigureAwait(false); } catch (OperationCanceledException) { break; } } });
                        try {
                            if((request.Operation.StartsWith("Install:") || request.Operation.StartsWith("Upgrade:"))) {
                                result = await new Toolkit.Software.DirectInstaller().InstallResultAsync(request.Operation.Substring(request.Operation.IndexOf(':')+1), sink, operationStop.Token, request.Operation.StartsWith("Upgrade:")).ConfigureAwait(false);
                            } else if(request.Operation.StartsWith("Dhcp") || request.Operation.Contains("Service:")) result = await Toolkit.Windows.QuickRepair.ExecuteAsync(request.Operation,sink).ConfigureAwait(false);
                            else result = await Toolkit.Windows.MaintenanceWorkflow.ExecuteAsync(request.Operation,sink,operationStop.Token).ConfigureAwait(false);
                        }
                        catch(OperationCanceledException) { sink(new OutputChunk { Channel="status", Text="Cancelled at safe checkpoint before installer execution.\r\n" }); result = new ProcessResult { ExitCode=1223 }; }
                        catch(Exception e) { sink(new OutputChunk { Channel="stderr", Text=e.GetType().Name+": "+e.Message+"\r\n" }); result = new ProcessResult { ExitCode=1, Verdict=ChangeVerdict.Failed.ToString(), Summary=e.GetType().Name+": "+e.Message }; }
                        finally { beatStop.Cancel(); }
                        await beats.ConfigureAwait(false);
                    }
                }
            }
            var complete = NewMessage("Complete", request.Operation, request.Session, request.Request, request.Nonce); complete.ExitCode = result.ExitCode; complete.Verdict=result.Verdict;complete.NativeExitCode=result.NativeExitCode;complete.Text=result.Summary==null?null:result.Summary.Substring(0,Math.Min(2048,result.Summary.Length));
            log.Write("ProcessComplete", request.Operation, "ProcessCompleted", "Exit code: " + result.ExitCode, result.DurationMs, request.Request);
            await send(complete).ConfigureAwait(false);
            return 0;
            }
        }
        private static string[] SubArray(string[] values) { var r = new string[values.Length - 1]; Array.Copy(values, 1, r, 0, r.Length); return r; }
    }
}
