using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Reflection;
using Microsoft.Win32.SafeHandles;

namespace Toolkit.Privilege
{
    internal static class NativeSecurity
    {
        internal static string ImagePath { get { return Process.GetCurrentProcess().MainModule.FileName; } }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [StructLayout(LayoutKind.Sequential)] private struct FileInformation
        { public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
        internal static void RejectReparse(SafeFileHandle handle)
        { FileInformation info; if (!GetFileInformationByHandle(handle, out info)) throw new Win32Exception(); if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse handle was rejected."); }
        internal static FileStream OpenPinnedFile(string path)
        {
            var handle = CreateFile(path, 0x80000000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
            try { RejectReparse(handle); return new FileStream(handle, FileAccess.Read); } catch { handle.Dispose(); throw; }
        }
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder value, ref int size);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int info, out int value, int size, out int needed);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool SetDllDirectory(string path);
        [DllImport("kernel32.dll")] private static extern bool SetDefaultDllDirectories(uint flags);
        public static void HardenDllSearch() { SetDllDirectory(""); SetDefaultDllDirectories(0x800); }
        public static bool IsElevated(Process process)
        {
            SafeAccessTokenHandle token;
            if (!OpenProcessToken(process.Handle, 8, out token)) throw new Win32Exception();
            using (token) { int value, size; if (!GetTokenInformation(token, 20, out value, 4, out size)) throw new Win32Exception(); return value != 0; }
        }
        public static string ProcessImage(Process process)
        { var value = new StringBuilder(32768); int size = value.Capacity; if (!QueryFullProcessImageName(process.Handle, 0, value, ref size)) throw new Win32Exception(); return value.ToString(); }
        public static string Hash(Stream stream)
        { stream.Position = 0; using (var hash = SHA256.Create()) { var value = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""); stream.Position = 0; return value; } }
        public static void VerifyPeer(Process peer, string expectedImage, long expectedStart, int expectedSession, bool mustElevate)
        {
            if (peer.HasExited || peer.StartTime.ToUniversalTime().Ticks != expectedStart || peer.SessionId != expectedSession) throw new InvalidDataException("Worker peer process identity/session mismatch.");
            if (!String.Equals(Path.GetFullPath(ProcessImage(peer)), Path.GetFullPath(expectedImage), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Worker peer image mismatch.");
            if (mustElevate && !IsElevated(peer)) throw new InvalidDataException("Worker does not have an elevated token.");
        }
        public static string CreatePrivateLogDirectory()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IT-Admin-Toolkit", "Logs");
            var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
            using (var identity = WindowsIdentity.GetCurrent())
            {
                foreach (var sid in new[] { identity.User, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                    acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                var dir = new DirectoryInfo(path);
                // Logs are never executable inputs; refuse link redirection instead of following it.
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                for (var current = dir; current != null; current = current.Parent)
                    if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Log location contains a reparse point.");
                dir.Create(acl); dir.SetAccessControl(acl);
            }
            return path;
        }
    }
    internal sealed class ImageLease : IDisposable
    {
        private readonly List<IDisposable> handles = new List<IDisposable>();
        public string Hash { get; private set; }
        public ImageLease(string image)
        {
            try
            {
                image = Path.GetFullPath(image);
                var drive = new DriveInfo(Path.GetPathRoot(image));
                if (image.StartsWith(@"\\") || !String.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) throw new IOException("Elevated operations require a trusted local NTFS application folder. Copy the portable folder to a local NTFS disk.");
                var ancestors = new Stack<string>(); for (var d = new DirectoryInfo(Path.GetDirectoryName(image)); d != null; d = d.Parent) ancestors.Push(d.FullName);
                foreach (var name in ancestors)
                {
                    if ((File.GetAttributes(name) & FileAttributes.ReparsePoint) != 0) throw new IOException("Application directory contains a reparse point.");
                    // Read attributes, no write/delete sharing: lock the namespace while the worker uses this image.
                    var h = NativeSecurity.CreateFile(name, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                    if (h.IsInvalid) { h.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot guard the application namespace."); }
                    handles.Add(h);
                    NativeSecurity.RejectReparse(h);
                }
                if ((File.GetAttributes(image) & FileAttributes.ReparsePoint) != 0) throw new IOException("Application image is a reparse point.");
                var stream = NativeSecurity.OpenPinnedFile(image); handles.Add(stream); Hash = NativeSecurity.Hash(stream);
                foreach (var dll in Directory.GetFiles(Path.GetDirectoryName(image), "*.dll"))
                    throw new IOException("Unexpected loose DLL beside the portable executable. Use a clean trusted release folder before elevation.");
                string configPath = image + ".config";
                if (!File.Exists(configPath)) throw new IOException("The portable runtime configuration is missing. Keep the EXE and .exe.config together.");
                if ((File.GetAttributes(configPath) & FileAttributes.ReparsePoint) != 0) throw new IOException("Runtime configuration is a reparse point.");
                var config = NativeSecurity.OpenPinnedFile(configPath); handles.Add(config);
                using (var expected = Assembly.GetExecutingAssembly().GetManifestResourceStream("Toolkit.App.config"))
                { if (expected == null || NativeSecurity.Hash(config) != NativeSecurity.Hash(expected)) throw new IOException("Runtime configuration differs from the embedded trusted build configuration. Restore the complete portable release."); }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { for (int i = handles.Count - 1; i >= 0; i--) handles[i].Dispose(); handles.Clear(); }
    }
    internal sealed class DirectoryLease : IDisposable
    {
        private readonly List<SafeFileHandle> handles = new List<SafeFileHandle>();
        public DirectoryLease(string path)
        {
            try
            {
                path = Path.GetFullPath(path);
                if (path.StartsWith(@"\\") || !String.Equals(new DriveInfo(Path.GetPathRoot(path)).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) throw new IOException("Executable staging requires local NTFS storage.");
                var chain = new Stack<string>(); for (var d = new DirectoryInfo(path); d != null; d = d.Parent) chain.Push(d.FullName);
                foreach (var name in chain)
                { var h = NativeSecurity.CreateFile(name, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero); if (h.IsInvalid) { h.Dispose(); throw new Win32Exception(); } handles.Add(h); NativeSecurity.RejectReparse(h); }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { for (int i = handles.Count - 1; i >= 0; i--) handles[i].Dispose(); handles.Clear(); }
    }
}
