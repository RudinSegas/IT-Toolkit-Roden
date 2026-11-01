using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Toolkit.Software
{
    public sealed class PackageDefinition
    {
        public string Name { get; private set; }
        public string Id { get; private set; }
        public string NamePattern { get; private set; }
        public string PublisherPattern { get; private set; }
        public string Page { get; private set; }
        public PackageDefinition(string name, string id, string pattern, string publisher, string page)
        { Name = name; Id = id; NamePattern = pattern; PublisherPattern = publisher; Page = page; }
        public override string ToString() { return Name + " · " + Id; }
    }
    public static class Catalog
    {
        public static PackageDefinition[] All()
        {
            return new[] {
                new PackageDefinition("Google Chrome", "Google.Chrome", @"^Google Chrome$", @"Google", "https://www.google.com/chrome/"),
                new PackageDefinition("Mozilla Firefox", "Mozilla.Firefox", @"^Mozilla Firefox", @"Mozilla", "https://www.mozilla.org/firefox/"),
                new PackageDefinition("7-Zip", "7zip.7zip", @"^7-Zip", @"Igor Pavlov", "https://www.7-zip.org/download.html"),
                new PackageDefinition("Adobe Acrobat Reader", "Adobe.Acrobat.Reader.64-bit", @"^Adobe (?:Acrobat Reader|Reader)(?:\s|$)", @"Adobe", "https://get.adobe.com/reader/"),
                new PackageDefinition("VLC Media Player", "VideoLAN.VLC", @"^VLC media player", @"VideoLAN", "https://www.videolan.org/vlc/"),
                new PackageDefinition("Notepad++", "Notepad++.Notepad++", @"^Notepad\+\+", @"Don Ho|Notepad\+\+", "https://notepad-plus-plus.org/downloads/"),
                new PackageDefinition("Visual Studio Code", "Microsoft.VisualStudioCode", @"^Microsoft Visual Studio Code", @"Microsoft", "https://code.visualstudio.com/download"),
                new PackageDefinition("Microsoft PowerToys", "Microsoft.PowerToys", @"PowerToys", @"Microsoft", "https://learn.microsoft.com/windows/powertoys/install"),
                new PackageDefinition("Java — Temurin 17 JRE (LTS)", "EclipseAdoptium.Temurin.17.JRE", @"Temurin.*17.*JRE|Temurin JRE.*17", @"Eclipse", "https://adoptium.net/temurin/releases/?version=17"),
                new PackageDefinition("Java — Temurin 21 JRE (LTS)", "EclipseAdoptium.Temurin.21.JRE", @"Temurin.*21.*JRE|Temurin JRE.*21", @"Eclipse", "https://adoptium.net/temurin/releases/?version=21"),
                new PackageDefinition("Java — Microsoft OpenJDK 17 (LTS)", "Microsoft.OpenJDK.17", @"Microsoft.*OpenJDK.*17|Microsoft Build of OpenJDK.*17", @"Microsoft", "https://learn.microsoft.com/java/openjdk/download"),
                new PackageDefinition("Java — Microsoft OpenJDK 21 (LTS)", "Microsoft.OpenJDK.21", @"Microsoft.*OpenJDK.*21|Microsoft Build of OpenJDK.*21", @"Microsoft", "https://learn.microsoft.com/java/openjdk/download")
            };
        }
        public static PackageDefinition Find(string id) { var d = All().FirstOrDefault(x => x.Id == id); if (d == null) throw new ArgumentException("Package is not in the approved catalog."); return d; }
        public static string[] InstallArguments(string id, bool update, string scope)
        {
            Find(id); if (scope != "user" && scope != "machine" && scope != "default") throw new ArgumentException("Invalid install scope.");
            var values = new List<string> { update ? "upgrade" : "install", "--id", id, "--exact", "--source", "winget", "--dependency-source", "winget", "--architecture", "x64", "--silent", "--disable-interactivity", "--accept-package-agreements", "--accept-source-agreements" };
            if (id == "Notepad++.Notepad++") { values.Add("--installer-type"); values.Add("wix"); }
            if (scope != "default") { values.Add("--scope"); values.Add(scope); }
            return values.ToArray();
        }
    }
    public sealed class InstalledApplication
    {
        public string Name { get; set; }
public string Publisher { get; set; }
public string Version { get; set; }
public string Scope { get; set; }
public string Architecture { get; set; }
public string InstallDate { get; set; }
public string Source { get; set; }
public int RegistryView { get; set; }
public string RemovalRestriction { get; set; }
public bool HasUninstallCommand { get; set; }
public bool IsProtected { get { return !String.IsNullOrWhiteSpace(RemovalRestriction); } }
public bool CanUninstall { get { return !IsProtected && HasUninstallCommand && UninstallPolicy.ValidIdentity(this); } }
public string UninstallAvailability { get { return IsProtected ? RemovalRestriction : !HasUninstallCommand ? "No registered uninstall command; use Windows Installed apps." : "Registered uninstaller available; confirmation required."; } }
    }
    public static class InstallationVerification
    {
        public static string Classify(int exitCode, string beforeVersion, string afterVersion, bool exactIdentity, bool update)
        {
            if (exitCode != 0 && exitCode != 3010 && exitCode != 1641) return "Installation failed";
            if (!exactIdentity || String.IsNullOrWhiteSpace(afterVersion) || afterVersion == "Unknown") return "Verification inconclusive";
            if (exitCode == 3010 || exitCode == 1641) return "Reboot required — package identity detected; verify after restart";
            if (update && String.Equals(beforeVersion, afterVersion, StringComparison.Ordinal)) return "Verification inconclusive — no version change detected";
            return update ? "Update completed — identity and version change verified" : "Installed successfully — identity/version verified";
        }
    }
}
