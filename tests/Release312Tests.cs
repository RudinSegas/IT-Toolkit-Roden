using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Toolkit.Core;
using Toolkit.Windows;
using Toolkit.Software;
namespace Toolkit.Tests
{
    public static partial class Suite
    {
        private static void Run312Core()
        {
            var now=DateTimeOffset.UtcNow;Check(CategoryCachePolicy.Fresh(now.AddSeconds(-30).ToString("o"),now),"recent category cached");Check(!CategoryCachePolicy.Fresh(now.AddMinutes(-3).ToString("o"),now)&&!CategoryCachePolicy.Fresh(null,now),"stale/initial category collects");Check(!CategoryCachePolicy.Accept(1,2,"Storage Health","Network Diagnostics+")&&CategoryCachePolicy.Accept(2,2,"Storage Health","Storage Health"),"stale category completion rejected");
            Check(DiagnosticMeaning.Capacity(1e12)=="1.00 TB"&&DiagnosticMeaning.Capacity(500e9)=="500.0 GB"&&DiagnosticMeaning.Capacity(null)=="Not Available","storage decimal units/missing capacity");
            Check(DiagnosticMeaning.DiskHealth(0)=="Healthy"&&DiagnosticMeaning.DiskHealth(1)=="Warning"&&DiagnosticMeaning.DiskHealth(2)=="Critical"&&DiagnosticMeaning.DiskHealth(null)=="Unknown"&&DiagnosticMeaning.DiskHealth(99)=="Unknown","documented storage health/missing data");
            Check(DiagnosticMeaning.DiskOperation(2)=="OK"&&DiagnosticMeaning.DiskOperation(53268)=="Unknown"&&DiagnosticMeaning.DiskOperation(null)=="Unknown","storage operational status mapping");
            Check(DiagnosticMeaning.DiskOperations(new ushort[]{2,3})=="OK, Degraded"&&DiagnosticMeaning.DiskOperations(new ushort[0])=="Unknown","multiple operational statuses retained");
            Check(DiagnosticMeaning.Media(null,17).Contains("NVMe")&&DiagnosticMeaning.Media(null,17).Contains("unknown"),"NVMe bus does not fabricate media type");
            Check(DiagnosticMeaning.State("Observed")==""&&DiagnosticMeaning.State("Unavailable / access denied")=="Not Available","ordinary values have no invented health verdict");
            Check(DiagnosticMeaning.Problem(28)=="Driver not installed"&&DiagnosticMeaning.Problem(43).Contains("reported a problem"),"device codes understandable");
            Check(DiagnosticMeaning.State("Not Available")=="Not Available"&&DiagnosticMeaning.State("Critical")=="Critical"&&DiagnosticMeaning.State("Healthy")=="Healthy","actionable health states survive card mapping");
            var raw=new DiagnosticSnapshot{Section="Domain Diagnostics"};raw.Add("Authentication / secure channel","Trusted connection status = 0 0x0 NERR_Success","Query returned evidence");
            Check(HelpdeskDiagnostics.DomainSummary(raw).Records.Single().State=="Healthy","explicit Netlogon success evidence mapped");
            raw.Records[0].Value="The command completed successfully.";Check(HelpdeskDiagnostics.DomainSummary(raw).Records.Single().State=="Unknown","generic command success cannot certify trust");
            raw.Records[0].Value="Trusted connection status = 0 0x0 NERR_Success";raw.Records[0].State="Failed";Check(HelpdeskDiagnostics.DomainSummary(raw).Records.Single().State=="Unknown","failed query cannot certify trust");
            var service=new ServiceRow{Name="Spooler",Status="Running",AcceptStop=true,Startup="Auto"};Check(!service.CanStart&&service.CanStop&&service.CanRestart,"running service applicable operations");service.AcceptStop=false;Check(!service.CanStop&&!service.CanRestart&&service.Explanation.Contains("does not accept Stop"),"non-stoppable service explained");service.Status="Stopped";service.Startup="Disabled";Check(!service.CanStart,"disabled service cannot start");service.Startup="Manual";Check(service.CanStart&&!service.CanStop,"stopped service actions");
            foreach(string bad in new[]{"StartService:Spooler' OR 1=1","StopService:..\\cmd","RestartService:WinDefend","Service:Spooler","StartService:Spooler&cmd"})Check(!ServicePolicy.Operation(bad),"typed service operation rejects injection/protected service");Check(ServicePolicy.Operation("RestartService:WSearch"),"supported selected service admitted without executable input");
            string query=ServiceEventBrowser.EventQuery("Critical",7,"41","Microsoft-Windows-Kernel-Power",1234);Check(query.Contains("Level=1")&&query.Contains("EventID=41")&&query.Contains("EventRecordID<1234")&&query.Contains("Provider[@Name="),"event filters/pagination server-side");
            Throws(()=>ServiceEventBrowser.EventQuery("Error",31,"","",0),"bounded event time range");Throws(()=>ServiceEventBrowser.EventQuery("All",1,"1 or 1=1","",0),"event ID injection rejected");Throws(()=>ServiceEventBrowser.EventQuery("All",1,"","x'] or *",0),"source XPath injection rejected");Check(ServiceEventBrowser.Logs.Contains("Security")&&ServiceEventBrowser.Level(4)=="Information","security source/information severity offered");
            var policy=UserActions.GroupPolicy(@"C:\Windows\System32");Check(policy.UseShellExecute&&!policy.RedirectStandardInput&&!policy.RedirectStandardOutput&&!policy.RedirectStandardError&&!policy.CreateNoWindow&&policy.WindowStyle==ProcessWindowStyle.Normal&&String.IsNullOrEmpty(policy.Verb),"interactive shell unredirected/current-user/no runas");Check(policy.Arguments.Contains("/k")&&policy.Arguments.Contains("gpupdate.exe")&&policy.Arguments.Contains("/force")&&!policy.Arguments.Contains("/boot")&&!policy.Arguments.Contains("/logoff"),"fixed GPUpdate command remains open with manual prompts");
            var reveal=UserActions.Reveal(Path.GetFullPath("fixture שלום & test.exe"));Check(reveal.Arguments.StartsWith("/select,")&&reveal.UseShellExecute,"Explorer selects Unicode/special-character path without installer execution");
            string lenovo="System Update Windows 11 <a href='https://download.lenovo.com/pccbbs/thinkvantage_en/system_update_5.08.04.85.exe'>Download</a>";Check(VendorUtilityDownload.ResolveLenovo(lenovo).EndsWith(".exe"),"official Lenovo dynamic resolver fixture");Throws(()=>VendorUtilityDownload.ResolveLenovo(lenovo.Replace("download.lenovo.com","evil.invalid")),"Lenovo foreign source rejected");
            string classic="<a href='https://www.dell.com/support/home/en-us/drivers/driversdetails?driverid=fixture'>Dell Command | Update Application (Classic)</a>";Check(VendorUtilityDownload.ClassicDetails(classic).Contains("driverid=fixture"),"explicit Classic link resolver");Throws(()=>VendorUtilityDownload.ClassicDetails(classic.Replace("(Classic)","Universal")),"Universal never substituted for Classic");Check(VendorUtilityDownload.ResolveClassic("Windows 11 x64 <title>Dell Command Update Application</title> https://dl.dell.com/FOLDER123/Dell-Command-Update-Application_TEST_WIN64_5.7.EXE").Contains("dl.dell.com"),"Classic x64 official download fixture");Throws(()=>VendorUtilityDownload.ResolveClassic("Windows 11 x64 <title>Dell Command Update Universal</title> https://dl.dell.com/FOLDER/x.exe"),"Universal driver page rejected");
            Check(DirectPolicy.Allowed("VendorUtility.Lenovo",new Uri("https://download.lenovo.com/a"))&&!DirectPolicy.Allowed("VendorUtility.DellClassic",new Uri("https://downloads.dell.com.evil.invalid/a")),"vendor redirect boundaries");Check(!Operations.IsElevated("Install:VendorUtility.Lenovo"),"vendor download is not elevated installation");
            Check(Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCompanyAttribute),false).Cast<AssemblyCompanyAttribute>().Single().Company=="Roden Segas","exact owner metadata");
        }
    }
}
