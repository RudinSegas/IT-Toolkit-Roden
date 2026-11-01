using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Toolkit.Core;

namespace Toolkit.Software
{
    [DataContract] internal sealed class ExportData { [DataMember] public ExportSource[] Sources { get; set; } }
    [DataContract] internal sealed class ExportSource { [DataMember] public ExportPackage[] Packages { get; set; } }
    [DataContract] internal sealed class ExportPackage { [DataMember] public string PackageIdentifier { get; set; } [DataMember] public string Version { get; set; } }
    internal sealed class InventoryEvidence { public bool Queried; public string Version; }
    [DataContract] public sealed class SourceEvidence
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string Type { get; set; }
        [DataMember] public string Arg { get; set; }
        [DataMember] public string Identifier { get; set; }
        public bool Approved { get { return Name == "winget" && Type == "Microsoft.PreIndexed.Package" && Arg != null && Arg.TrimEnd('/') == "https://cdn.winget.microsoft.com/cache" && Identifier == "Microsoft.Winget.Source_8wekyb3d8bbwe"; } }
    }
    public sealed class SoftwareService
    {
        private readonly ProcessEngine engine = new ProcessEngine();
        private async Task VerifySourceAsync(string path, CancellationToken token)
        {
            var json = new StringBuilder();
            var result = await engine.RunAsync(path, new[] { "source", "export", "--name", "winget", "--disable-interactivity" }, Encoding.UTF8,
                c => { if (c.Channel == "stdout") lock (json) { if (json.Length < 32768) json.Append(c.Text); } }, token, true, 30).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new InvalidOperationException("WinGet source verification failed. Update/repair App Installer or ask the administrator to restore the approved source; the toolkit does not reset policy or sources.");
            SourceEvidence source;
            try { using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToString().Trim().TrimStart('\uFEFF')))) source = (SourceEvidence)new DataContractJsonSerializer(typeof(SourceEvidence)).ReadObject(stream); }
            catch { throw new InvalidDataException("WinGet did not return a supported structured source record. No installer was run."); }
            if (source == null || !source.Approved) throw new InvalidDataException("The source named winget is not the approved Microsoft community source. No installer was run; source settings were not changed.");
        }
        public static List<InstalledApplication> Inventory()
        {
            var all = new List<InstalledApplication>();
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(hive, view))
                    using (var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstall == null) continue;
                        foreach (string child in uninstall.GetSubKeyNames().Take(4096))
                        try { using (var key = uninstall.OpenSubKey(child)) { string name = Convert.ToString(key.GetValue("DisplayName")); if (name.Length > 0) all.Add(new InstalledApplication { Name = name, Publisher = Convert.ToString(key.GetValue("Publisher")), Version = Convert.ToString(key.GetValue("DisplayVersion", "Unknown")), Scope = hive == RegistryHive.LocalMachine ? "machine" : "user", Architecture = view == RegistryView.Registry64 ? "x64" : "x86" }); } } catch { }
                    }
                } catch { }
            }
            return all;
        }
        public static string Detected(PackageDefinition definition)
        {
            var found = Inventory().Where(x => Regex.IsMatch(x.Name, definition.NamePattern, RegexOptions.IgnoreCase) && Regex.IsMatch(x.Publisher, definition.PublisherPattern, RegexOptions.IgnoreCase)).ToArray();
            return found.Length == 0 ? "Not detected / inaccessible" : "Detected: " + String.Join("; ", found.Select(x => x.Version + " (" + x.Scope + ", " + x.Architecture + ")")) + " — registry evidence; exact identity checked after installation";
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint length, IntPtr buffer);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagePathByFullName(string name, ref uint length, StringBuilder path);
        public static string FindWinget()
        {
            uint count = 0, length = 0;
            int status = GetPackagesByPackageFamily("Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", ref count, IntPtr.Zero, ref length, IntPtr.Zero);
            if (status != 122 || count == 0 || count > 64 || length > 65536) throw new FileNotFoundException("WinGet/App Installer is missing or not registered for this Windows account. Use Install / repair WinGet, then Detect again.");
            IntPtr names = Marshal.AllocHGlobal((int)count * IntPtr.Size), buffer = Marshal.AllocHGlobal((int)length * 2);
            try
            {
                if (GetPackagesByPackageFamily("Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", ref count, names, ref length, buffer) != 0) throw new IOException("Cannot resolve App Installer package registration.");
                for (int i = 0; i < count; i++)
                {
                    string full = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, i * IntPtr.Size)); uint size = 0;
                    if (GetPackagePathByFullName(full, ref size, null) != 122 || size > 32768) continue;
                    var path = new StringBuilder((int)size);
                    if (GetPackagePathByFullName(full, ref size, path) != 0) continue;
                    string candidate = Path.Combine(path.ToString(), "winget.exe");
                    if (File.Exists(candidate)) { Signature.VerifyMicrosoft(candidate); return candidate; }
                }
            }
            finally { Marshal.FreeHGlobal(names); Marshal.FreeHGlobal(buffer); }
            throw new FileNotFoundException("The registered Microsoft App Installer package does not expose a trusted WinGet executable for this account.");
        }
        public async Task<string> DetectAsync(Action<OutputChunk> output, CancellationToken token)
        {
            string path = FindWinget(); var result = await engine.RunAsync(path, new[] { "--version" }, Encoding.UTF8, output, token, true, 30).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new InvalidOperationException("WinGet is installed but failed its availability probe. Exit 0x" + unchecked((uint)result.ExitCode).ToString("X8") + ". Review policy/App Installer logs.");
            await VerifySourceAsync(path, token).ConfigureAwait(false);
            return "WinGet availability verified for the current account.\r\n" + path;
        }
        public async Task<string> DetailsAsync(string id, Action<OutputChunk> output, CancellationToken token)
        {
            Catalog.Find(id); string winget = FindWinget();
            await VerifySourceAsync(winget, token).ConfigureAwait(false);
            var r = await engine.RunAsync(winget, new[] { "show", "--id", id, "--exact", "--source", "winget", "--disable-interactivity", "--accept-source-agreements" }, Encoding.UTF8, output, token, true, 90).ConfigureAwait(false);
            return "Catalog metadata exit: " + r.ExitCode + ". Original WinGet metadata is shown; no localized table was guessed into a version. No installation was performed.";
        }
        private static string TempDirectory()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IT-Admin-Toolkit", "Data", Guid.NewGuid().ToString("N"));
            var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
            using (var id = WindowsIdentity.GetCurrent()) acl.AddAccessRule(new FileSystemAccessRule(id.User, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            for (var d = new DirectoryInfo(Path.GetDirectoryName(path)); d != null; d = d.Parent) if ((d.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Data directory has a reparse point.");
            new DirectoryInfo(path).Create(acl); return path;
        }
        private async Task<InventoryEvidence> ExactVersionAsync(string winget, string id, Action<OutputChunk> sink, CancellationToken token)
        {
            string directory = TempDirectory(), file = Path.Combine(directory, "inventory.json");
            try
            {
                var result = await engine.RunAsync(winget, new[] { "export", "--output", file, "--include-versions", "--source", "winget", "--accept-source-agreements", "--disable-interactivity" }, Encoding.UTF8, null, token, true, 90).ConfigureAwait(false);
                if (result.ExitCode != 0 || !File.Exists(file) || new FileInfo(file).Length > 4 * 1024 * 1024) return new InventoryEvidence();
                using (var input = File.OpenRead(file))
                {
                    var data = (ExportData)new DataContractJsonSerializer(typeof(ExportData), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 100000 }).ReadObject(input);
                    var matches = (data.Sources ?? new ExportSource[0]).SelectMany(x => x.Packages ?? new ExportPackage[0]).Where(x => x.PackageIdentifier == id && !String.IsNullOrWhiteSpace(x.Version)).ToArray();
                    return new InventoryEvidence { Queried = data.Sources != null && matches.Length <= 1, Version = matches.Length == 1 ? matches[0].Version : null };
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { return new InventoryEvidence(); }
            finally { try { if (File.Exists(file)) File.Delete(file); Directory.Delete(directory, false); } catch { } }
        }
        public async Task<string> InstallAsync(string id, bool update, string scope, Action<OutputChunk> output, CancellationToken token)
        {
            var d = Catalog.Find(id); string winget = FindWinget();
            await VerifySourceAsync(winget, token).ConfigureAwait(false);
            var beforeEvidence = await ExactVersionAsync(winget, id, output, token).ConfigureAwait(false);
            string before = beforeEvidence.Version;
            if (!update && !String.IsNullOrWhiteSpace(before)) return d.Name + ": Already installed (exact WinGet identity " + id + ", version " + before + "). Use Update for an upgrade. Scope was not changed.";
            if(!beforeEvidence.Queried)return d.Name+": Verification Inconclusive — exact installed state unavailable; no installer executed.";
            if(update&&!String.IsNullOrWhiteSpace(before))
            {
                // Winget performs its own source-version decision; an independent official version match can skip it earlier.
                string current=await new DirectInstaller().CheckWithoutElevationAsync(id,true,token).ConfigureAwait(false);
                if(current!=null&&(scope=="default"||scope==(id=="Microsoft.VisualStudioCode"?"user":"machine")))return current;
                StateWorkflow.Emit(output,"Check: Installed — Update Status Unknown. Decision: explicit update; WinGet must resolve its configured official source and decide whether an upgrade is applicable.");
            }
            token.ThrowIfCancellationRequested();
            var r = await engine.RunAsync(winget, Catalog.InstallArguments(id, update, scope), Encoding.UTF8, output, token, false).ConfigureAwait(false);
            // Verification must run even when cancellation was requested during an active installer.
            var afterEvidence = await ExactVersionAsync(winget, id, output, CancellationToken.None).ConfigureAwait(false);
            string after = afterEvidence.Version;
            bool scopeMatched = scope == "default" || Inventory().Any(x => x.Scope == scope && Regex.IsMatch(x.Name, d.NamePattern, RegexOptions.IgnoreCase) && Regex.IsMatch(x.Publisher, d.PublisherPattern, RegexOptions.IgnoreCase));
            string result = InstallationVerification.Classify(r.ExitCode, before, after, beforeEvidence.Queried && afterEvidence.Queried && after != null && scopeMatched, update);
            return d.Name + ": " + result + "\r\nExit: " + r.ExitCode + " (0x" + unchecked((uint)r.ExitCode).ToString("X8") + ")\r\nBefore: " + (before ?? "Not established") + "\r\nAfter: " + (after ?? "Not established") + "\r\n" + Detected(d) + (r.CancelPending ? "\r\nCancellation requested: active installer was allowed to finish; later packages were skipped." : "") + "\r\nWinGet controls package hashes, source and installer UAC. Exact export identity/version is evidence, not application launch or license validation.";
        }
    }
}
