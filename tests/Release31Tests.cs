using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Toolkit.Core;
using Toolkit.Windows;
using Toolkit.Software;
using Toolkit.Reporting;
namespace Toolkit.Tests
{
    public static partial class Suite
    {
        private static void Run31Core()
        {
            Check(RebootDetection.Classify(new bool?[]{false,false})=="No Known Reboot Pending","reboot negative evidence");
            Check(RebootDetection.Classify(new bool?[]{false,null})=="Unknown","reboot inaccessible evidence");
            Check(RebootDetection.Classify(new bool?[]{true,null})=="Reboot Required","reboot positive wins");
            Check(Operations.IsElevated("DnsFlush")&&Operations.Command("NetFx3").Contains("/NoRestart"),"fixed repair operations no restart");
            Check(Operations.IsElevated("DhcpRenew:12345678-1234-1234-1234-123456789abc"),"typed DHCP GUID admitted");
            foreach(string bad in new[]{"DhcpRenew:Ethernet & cmd","RestartService:WinDefend","Install:cmd.exe","Upgrade:Microsoft.VisualStudioCode","Install:Google.Chrome --override"})Check(!Operations.IsElevated(bad),"restricted privileged action rejected");
            foreach(var p in Catalog.All()) {Check(DirectPolicy.Allowed(p.Id,new Uri("https://"+DirectPolicy.Hosts(p.Id)[0]+"/")),"catalog official origin accepted");Check(!DirectPolicy.Allowed(p.Id,new Uri("https://evil.invalid/")),"foreign origin rejected");}
            Check(!DirectPolicy.Allowed("Google.Chrome",new Uri("http://dl.google.com/a"))&&!DirectPolicy.Allowed("Google.Chrome",new Uri("https://dl.google.com.evil.invalid/a")),"TLS/origin boundaries");
            Throws(()=>DirectPolicy.CheckHash(new string('a',64),new string('b',64)),"hash mismatch blocks");
            DirectPolicy.CheckHash(new string('a',64),new string('A',64));Check(true,"hash match");
            Check(!System.Text.RegularExpressions.Regex.IsMatch("Adobe Acrobat Pro",Catalog.Find("Adobe.Acrobat.Reader.64-bit").NamePattern),"Acrobat Pro is not Reader evidence");
            Check(InstalledInventory.Cell("=cmd").StartsWith("\"'="),"CSV formula escape");
            Check(InstalledInventory.Csv(new[]{new InstalledApplication{Name="a,b",Version="1",Publisher="x"}}).Contains("\"a,b\""),"CSV quoted inventory");
            string redacted=DiagnosticReports.Redact("UserAlice hostExample 192.0.2.12 AA:BB:CC:DD:EE:FF C:\\Users\\UserAlice\\secret.txt",new[]{"UserAlice","hostExample"});
            Check(!redacted.Contains("UserAlice")&&!redacted.Contains("hostExample")&&!redacted.Contains("192.0.2.12")&&!redacted.Contains("secret.txt"),"report identity/IP/MAC/path redaction");
            var before=new DiagnosticSnapshot{Section="Network Diagnostics+"};before.Add("DNS","192.0.2.1");var after=new DiagnosticSnapshot{Section=before.Section};after.Add("DNS","192.0.2.2");Check(DiagnosticSnapshot.Compare(before,after).Contains("192.0.2.2"),"network snapshot changes");
            var prepared=DiagnosticReports.Prepare(new[]{after},true,new string[0]);Check(!prepared[0].Records[0].Value.Contains("192.0.2.2"),"structured report field redaction");
            after.Add("Unsafe","<script>alert(1)</script>");Check(!DiagnosticReports.Html(new[]{after}).Contains("<script>"),"HTML output encodes content");
            Check(DirectInstaller.VersionMatches("v1.2.3","1.2.3.0")&&!DirectInstaller.VersionMatches("v1.2.3","1.1.0"),"installed version matches known release");
            Check(!new OoklaWebsiteProvider().SupportsNativeExecution,"Ookla licensing fallback cannot execute CLI");
            var release=new TaskCompletionSource<string>();var started=new ManualResetEventSlim();
            var pending=BoundedWork.Run("duplicate-fixture",()=>{started.Set();return release.Task;},1,CancellationToken.None);started.Wait();
            Throws(()=>BoundedWork.Run("duplicate-fixture",()=>Task.FromResult("bad"),1,CancellationToken.None).GetAwaiter().GetResult(),"duplicate provider refused");
            Throws(()=>pending.GetAwaiter().GetResult(),"bounded provider times out");
            Throws(()=>BoundedWork.Run("duplicate-fixture",()=>Task.FromResult("bad"),1,CancellationToken.None).GetAwaiter().GetResult(),"timed-out provider remains guarded");release.SetResult("done");
        }
        private static void Run31WindowsMetadata()
        {
            string json="{\"tag_name\":\"v1.2\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"7z1234-x64.exe\",\"browser_download_url\":\"https://github.com/ip7z/7zip/releases/download/v1.2/7z1234-x64.exe\",\"digest\":\"sha256:"+new string('a',64)+"\"}]}";
            var p=DirectInstaller.ParseRelease("7zip.7zip",json,"ip7z/7zip",@"^7z[0-9]+-x64\.exe$","Igor Pavlov",new[]{"/S"},true);
            Check(p.Hash.Length==64&&p.Version=="v1.2","official release metadata parser fixture");
            Throws(()=>DirectInstaller.ParseRelease("7zip.7zip",json.Replace("github.com/ip7z","github.com/attacker"),"ip7z/7zip",@"^7z[0-9]+-x64\.exe$","Igor Pavlov",new[]{"/S"},true),"foreign release repository blocked");
            var s=new DiagnosticSnapshot{Section="Fixture"};s.Add("text","<unsafe>");Check(DiagnosticReports.Json(new[]{s}).Contains("1.0.0"),"JSON report version");
        }
    }
}
