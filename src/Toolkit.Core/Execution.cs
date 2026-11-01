using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Toolkit.Core
{
    public static class Operations
    {
        private static readonly string[] elevated = { "WorkerProbe", "Sfc", "DismCheck", "DismScan", "DismRestore", "ResultComputer", "DnsFlush", "NetFx3" };
        public static bool IsElevated(string value) { if (elevated.Contains(value, StringComparer.Ordinal)) return true;
            if (value == null) return false;
            if ((value.StartsWith("Install:", StringComparison.Ordinal) || value.StartsWith("Upgrade:", StringComparison.Ordinal))) return Toolkit.Software.Catalog.All().Any(x => ("Install:" + x.Id == value || "Upgrade:" + x.Id == value) && x.Id != "Microsoft.VisualStudioCode");
            if (Toolkit.Windows.ServicePolicy.Operation(value))return true;
            return Regex.IsMatch(value, @"\A(?:DhcpRenew|DhcpRelease):[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z"); }
        public static string[] Command(string operation)
        {
            switch (operation)
            {
                case "DnsFlush": return new[] { "ipconfig.exe", "/flushdns" };
                case "NetFx3": return new[] { "dism.exe", "/Online", "/Enable-Feature", "/FeatureName:NetFx3", "/All", "/NoRestart" };
                case "Sfc": return new[] { "sfc.exe", "/scannow" };
                case "DismCheck": return new[] { "dism.exe", "/Online", "/Cleanup-Image", "/CheckHealth" };
                case "DismScan": return new[] { "dism.exe", "/Online", "/Cleanup-Image", "/ScanHealth" };
                case "DismRestore": return new[] { "dism.exe", "/Online", "/Cleanup-Image", "/RestoreHealth" };
                case "ResultUser": return new[] { "gpresult.exe", "/scope", "user", "/r" };
                case "ResultComputer": return new[] { "gpresult.exe", "/scope", "computer", "/r" };
                default: throw new ArgumentException("Operation is not allowlisted.");
            }
        }
    }
    public static class Arguments
    {
        public static string Join(IEnumerable<string> values) { return String.Join(" ", values.Select(Quote)); }
        public static string Quote(string value)
        {
            if (value == null || value.Any(c => c == '\0' || c == '\r' || c == '\n')) throw new ArgumentException("Invalid command argument.");
            if (value.Length > 0 && !value.Any(c => Char.IsWhiteSpace(c) || c == '"')) return value;
            return "\"" + Regex.Replace(Regex.Replace(value, "(\\\\*)\"", "$1$1\\\""), "(\\\\+)$", "$1$1") + "\"";
        }
        public static string Host(string text)
        {
            var host = (text ?? "").Trim(); System.Net.IPAddress ip;
            if (host.Length < 1 || host.Length > 253) throw new ArgumentException("Enter a hostname or IP address (1–253 characters).");
            if (System.Net.IPAddress.TryParse(host, out ip)) return ip.ToString();
            if (!Regex.IsMatch(host, "\\A[A-Za-z0-9][A-Za-z0-9.-]*\\.?\\z")) throw new ArgumentException("Use a hostname or IP, without spaces, URL or command characters.");
            foreach (var label in host.TrimEnd('.').Split('.')) if (label.Length == 0 || label.Length > 63 || label.StartsWith("-") || label.EndsWith("-")) throw new ArgumentException("Invalid hostname label.");
            return host;
        }
    }
    public sealed class TerminalBuffer
    {
        private readonly StringBuilder history = new StringBuilder(); private readonly StringBuilder line = new StringBuilder(); private int column; private bool escape;
        public bool Truncated { get; private set; }
        public void Reset() { history.Clear(); line.Clear(); column = 0; escape = false; Truncated = false; }
        public void Append(string text)
        {
            foreach (char c in text)
            {
                if (escape) { if (c >= '@' && c <= '~' && c != '[') escape = false; continue; }
                if (c == 27) { escape = true; continue; }
                if (c == '\r') column = 0;
                else if (c == '\n') { history.Append(line).Append(Environment.NewLine); line.Clear(); column = 0; }
                else if (c == '\b') column = Math.Max(0, column - 1);
                else if (c == '\t' || !Char.IsControl(c))
                { if (column < line.Length) line[column] = c; else line.Append(c); column++; }
                if (line.Length > 8192) { history.Append(line); line.Clear(); column = 0; }
                if (history.Length > 200000) { history.Remove(0, history.Length - 180000); Truncated = true; }
            }
            if (history.Length > 200000) { history.Remove(0, history.Length - 180000); Truncated = true; }
        }
        public override string ToString() { return (Truncated ? "[Recent output shown; export the retained transcript for more.]" + Environment.NewLine : "") + history + line; }
    }
    public static class ProgressParser
    {
        public static double? Parse(string text)
        {
            var matches = Regex.Matches(text, "(?<![\\d.,])(100|[0-9]{1,2})(?:[.,]([0-9]{1,2}))?\\s*%");
            if (matches.Count == 0) return null;
            double value; return Double.TryParse(matches[matches.Count - 1].Value.TrimEnd('%', ' ').Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value <= 100 ? (double?)value : null;
        }
    }
    public sealed class ProcessResult
    {
        public string Summary; public string Verdict; public int? NativeExitCode; public int ExitCode; public bool CancelPending; public double DurationMs; public int ProcessId;
        public bool RebootRequired { get { return ExitCode == 3010 || ExitCode == 1641; } }
    }
    // Byte-level asynchronous drains keep partial lines, CR updates and decoder state.
    public sealed class ProcessEngine
    {
        private static readonly ConcurrentDictionary<string, byte> active = new ConcurrentDictionary<string, byte>();
        public async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, Encoding encoding, Action<OutputChunk> output, CancellationToken cancel, bool safeToKill = false, int timeoutSeconds = 0, bool restrictedEnvironment = false)
        {
            var args = arguments.ToArray(); string key = executable + "|" + Arguments.Join(args);
            if (!active.TryAdd(key, 0)) throw new InvalidOperationException("The same command is already running in this process.");
            try { return await RunCoreAsync(executable,args,encoding,output,cancel,safeToKill,timeoutSeconds,restrictedEnvironment).ConfigureAwait(false); }
            finally { byte ignored; active.TryRemove(key, out ignored); }
        }
        private async Task<ProcessResult> RunCoreAsync(string executable, IEnumerable<string> arguments, Encoding encoding, Action<OutputChunk> output, CancellationToken cancel, bool safeToKill, int timeoutSeconds, bool restrictedEnvironment)
        {
            var clock = Stopwatch.StartNew(); long sequence = 0; int emittedCancellation = 0;
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(executable, Arguments.Join(arguments)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, WorkingDirectory = Environment.SystemDirectory.Length == 0 ? Environment.CurrentDirectory : Environment.SystemDirectory };
                process.EnableRaisingEvents = true;
                if (restrictedEnvironment)
                {
                    process.StartInfo.EnvironmentVariables["PATH"] = Environment.SystemDirectory + ";" + Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    process.StartInfo.EnvironmentVariables["PSModulePath"] = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\Modules");
                }
                var exited = new TaskCompletionSource<bool>(); process.Exited += (s, e) => exited.TrySetResult(true);
                cancel.ThrowIfCancellationRequested();
                TryOutput(output, new OutputChunk { Channel = "status", Text = "Starting " + Path.GetFileName(executable) + "...\r\n" });
                process.Start();
                TryOutput(output, new OutputChunk { Channel = "status", Text = "Running PID " + process.Id + "; stdin closed; interactive commands are not supported.\r\n" });
                process.StandardInput.Close();
                if (process.HasExited) exited.TrySetResult(true);
                using (var deadline = new CancellationTokenSource())
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, deadline.Token))
                {
                    if (timeoutSeconds > 0) deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                    using (linked.Token.Register(() =>
                    {
                        if (safeToKill) { try { if (!process.HasExited) process.Kill(); } catch { } }
                        else if (Interlocked.Exchange(ref emittedCancellation, 1) == 0) TryOutput(output, new OutputChunk { Channel = "status", Text = "Cancellation requested; active servicing/installer will finish safely.\r\n" });
                    }))
                    {
                        Func<string, string, OutputChunk> chunk = (channel, text) => new OutputChunk { Channel = channel, Text = text, Sequence = Interlocked.Increment(ref sequence) };
                        var stdout = DrainAsync(process.StandardOutput.BaseStream, encoding, "stdout", output, chunk);
                        var stderr = DrainAsync(process.StandardError.BaseStream, encoding, "stderr", output, chunk);
                        var cancellationSignal = new TaskCompletionSource<bool>();
                        using (linked.Token.Register(() => cancellationSignal.TrySetResult(true)))
                        {
                            if (safeToKill && await Task.WhenAny(exited.Task, cancellationSignal.Task).ConfigureAwait(false) != exited.Task)
                            {
                                if (await Task.WhenAny(exited.Task, Task.Delay(15000)).ConfigureAwait(false) != exited.Task)
                                    throw new TimeoutException("Termination could not be confirmed for PID " + process.Id + ". Operation may still be running; inspect it before retrying.");
                            }
                            await exited.Task.ConfigureAwait(false);
                        }
                        // A child that inherits redirected handles can outlive its launcher. Never hang the UI forever.
                        var drains = Task.WhenAll(stdout, stderr);
                        if (await Task.WhenAny(drains, Task.Delay(10000)).ConfigureAwait(false) != drains)
                        { process.StandardOutput.Close(); process.StandardError.Close(); TryOutput(output, chunk("status", "Output pipe remained open after process exit; final capture is incomplete.\r\n")); }
                        try { await drains.ConfigureAwait(false); } catch (ObjectDisposedException) { } catch (IOException) { }
                        if (safeToKill) { cancel.ThrowIfCancellationRequested(); if (deadline.IsCancellationRequested) throw new TimeoutException("Read-only command deadline exceeded."); }
                        TryOutput(output, chunk("status", "Process PID " + process.Id + " exited: " + process.ExitCode + "\r\n"));
                        return new ProcessResult { ProcessId = process.Id, ExitCode = process.ExitCode, DurationMs = clock.Elapsed.TotalMilliseconds, CancelPending = linked.IsCancellationRequested };
                    }
                }
            }
        }
        private static void TryOutput(Action<OutputChunk> output, OutputChunk chunk) { try { if (output != null) output(chunk); } catch { /* A UI/log failure cannot terminate servicing. */ } }
        public static async Task DrainAsync(Stream input, Encoding encoding, string channel, Action<OutputChunk> output, Func<string, string, OutputChunk> create)
        {
            var bytes = new byte[1024]; var chars = new char[4096]; var decoder = encoding.GetDecoder(); bool first = true;
            int n;
            while ((n = await input.ReadAsync(bytes, 0, bytes.Length).ConfigureAwait(false)) > 0)
            {
                int count = decoder.GetChars(bytes, 0, n, chars, 0, false);
                string text = new string(chars, 0, count);
                if (first && text.Length > 0) { text = text.TrimStart('\uFEFF'); first = false; }
                if (text.Length > 0) TryOutput(output, create(channel, text));
            }
            int tail = decoder.GetChars(new byte[0], 0, 0, chars, 0, true);
            if (tail > 0) TryOutput(output, create(channel, new string(chars, 0, tail)));
        }
    }
}
