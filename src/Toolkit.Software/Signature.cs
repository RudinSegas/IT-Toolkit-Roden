using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace Toolkit.Software
{
    public static class Signature
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct FileInfo { public int Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr File; public IntPtr Subject; }
        [StructLayout(LayoutKind.Sequential)] private struct TrustData
        { public int Size; public IntPtr Policy, Sip; public uint UI, Revocation, Union; public IntPtr File; public uint StateAction; public IntPtr State, Url; public uint Flags, Context; public IntPtr SignatureSettings; }
        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int WinVerifyTrust(IntPtr window, [In] ref Guid action, ref TrustData data);
        public static void VerifyTrusted(string path) { VerifyPublisher(path, null, false); }
        public static void VerifyMicrosoft(string path) { VerifyPublisher(path, "Microsoft Corporation", false); }
        public static void VerifyPublisher(string path, string publishers, bool unsignedWithVerifiedDigest)
        {
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var file = new FileInfo { Size = Marshal.SizeOf(typeof(FileInfo)), Path = path, File = held.SafeFileHandle.DangerousGetHandle() };
                var pointer = Marshal.AllocHGlobal(file.Size);
                Marshal.StructureToPtr(file, pointer, false);
                var data = new TrustData { Size = Marshal.SizeOf(typeof(TrustData)), UI = 2, Revocation = 1, Union = 1, File = pointer, StateAction = 1, Flags = 0x2080 };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                try
                {
                    int status = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                    if (status == unchecked((int)0x800B0100) && unsignedWithVerifiedDigest) return;
                    if (status != 0) throw new InvalidDataException("Authenticode trust validation failed (0x" + unchecked((uint)status).ToString("X8") + "). No file was executed.");
                    using (var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                        if (publishers != null && !Array.Exists(publishers.Split('|'), expected => String.Equals(cert.GetNameInfo(X509NameType.SimpleName, false), expected, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Unexpected installer publisher. Expected " + publishers + ".");
                }
                finally { data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); Marshal.DestroyStructure(pointer, typeof(FileInfo)); Marshal.FreeHGlobal(pointer); }
            }
        }
    }
}
