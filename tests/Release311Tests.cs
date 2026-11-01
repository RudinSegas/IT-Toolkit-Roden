using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Toolkit.Core;
using Toolkit.Software;
using Toolkit.Windows;
namespace Toolkit.Tests
{
    public static partial class Suite
    {
        private sealed class HttpFixture:HttpMessageHandler
        {
            public Func<int,HttpRequestMessage,HttpResponseMessage> Reply;public int Requests;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(Reply(++Requests,request));}
        }
        private static void Run311Core()
        {
            foreach(bool repair in new[]{false,true})foreach(bool? desired in new bool?[]{true,false,null})
                Check(StateWorkflow.Decide(new StateEvidence{Desired=desired},repair)==(desired==true&&!repair?ChangeVerdict.AlreadyConfigured:ChangeVerdict.VerificationInconclusive),"desired-state/explicit-repair decision matrix");
            Check(StateWorkflow.Decide(new StateEvidence{Desired=true,PendingRestart=true},false)==ChangeVerdict.RestartRequired,"pending state prevents duplicate configuration");
            foreach(int code in new[]{0,3010,1641,5})foreach(bool? desired in new bool?[]{true,false,null})
                Check(StateWorkflow.Verify(code,new StateEvidence{Desired=desired})==(code==5?ChangeVerdict.Failed:code==3010||code==1641?ChangeVerdict.RestartRequired:desired==true?ChangeVerdict.SuccessfullyChanged:desired==false?ChangeVerdict.Failed:ChangeVerdict.VerificationInconclusive),"independent postcheck/exit/reboot matrix");
            Check(StateWorkflow.Result(ChangeVerdict.Failed,"download failure").ExitCode==1,"no nonexistent installer exit dereference");
            foreach(string state in new[]{"Enabled","Disabled","Enable Pending","Disable Pending"})Check(MaintenanceWorkflow.ParseFeature("Feature Name : NetFx3\r\nState : "+state+"\r\n")==state,"feature state parser "+state);
            Check(MaintenanceWorkflow.ParseFeature("success exit 0")=="Unknown","missing feature evidence unknown");
            Check(MaintenanceWorkflow.ParseFeature("State : Disabled with Payload Removed")=="Disabled","payload-removed feature can be enabled");
            Check(MaintenanceWorkflow.FeatureDecision("Enabled")==ChangeVerdict.AlreadyConfigured,"NetFx3 already enabled skips execute");
            Check(MaintenanceWorkflow.FeatureDecision("Enable Pending")==ChangeVerdict.RestartRequired,"NetFx3 pending prevents duplicate servicing");
            Check(!QuickRepair.HasLease(new[]{"169.254.1.1","::1"})&&QuickRepair.HasLease(new[]{"192.0.2.1"}),"DHCP independent usable IPv4 evidence");
            Check(Operations.IsElevated("StartService:Spooler")&&Operations.IsElevated("StopService:BITS")&&!Operations.IsElevated("StopService:WinDefend"),"service allowlist preserved");
            Check(DirectInstaller.ExactVersion("v1.2.3","1.2.3.0")&&!DirectInstaller.ExactVersion("1.2.3.4","1.2.3.5"),"latest verification does not ignore fourth component");
            Check(!DirectInstaller.ExactVersion("Unknown","1.2.3")&&!DirectInstaller.ExactVersion("1.2.3",null),"unknown version never up to date");
            TestHttp().GetAwaiter().GetResult();
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Toolkit.MainWindow.xaml"))using(var reader=new StreamReader(stream))
            {
                string xaml=reader.ReadToEnd();var xml=new XmlDocument();xml.LoadXml(xaml);
                Check(xaml.Contains("IsNetworkDiagnostic")&&xaml.Contains("IsDeviceDiagnostic")&&xaml.Contains("IsSoftwareDiagnostic"),"contextual controls bound by category");
                Check(xaml.Contains("DiagnosticDetail, Mode=OneWay")&&xaml.Contains("OutputExpanded, Mode=TwoWay"),"read-only detail and collapsible activity contracts");
                Check(xaml.Contains("SelectedDiagnostic, Mode=TwoWay")&&xaml.Contains("Progress, Mode=OneWay"),"selection writable progress display-only");
            }
            Check(ExtendedDiagnostics.Sections.Contains("Overview")&&ExtendedDiagnostics.Sections.Contains("BIOS & Hardware")&&ExtendedDiagnostics.Sections.Length==10,"ten diagnostic categories retained");
        }
        private static async Task TestHttp()
        {
            var waits=new List<TimeSpan>();var logs=new List<string>();
            using(var handler=new HttpFixture{Reply=(n,r)=>{var response=new HttpResponseMessage(n<3?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK);if(n<3)response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(4));return response;}})
            using(var client=new HttpClient(handler))
            using(var response=await DownloadTransport.Get(client,"VideoLAN.VLC","https://download.videolan.org/test","Resolve",logs.Add,CancellationToken.None,(wait,token)=>{waits.Add(wait);return Task.FromResult(true);}))
            {Check(handler.Requests==3&&waits.All(x=>x.TotalSeconds>=4),"HTTP503 recovery respects Retry-After");Check(logs.Any(x=>x.Contains("Resolve")&&x.Contains("https://download.videolan.org/test")),"stage and exact URL retained");}
            using(var handler=new HttpFixture{Reply=(n,r)=>new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)})using(var client=new HttpClient(handler))
            {Throws(()=>DownloadTransport.Get(client,"VideoLAN.VLC","https://download.videolan.org/test","Download",null,CancellationToken.None,(w,t)=>Task.FromResult(true)).GetAwaiter().GetResult(),"persistent503 fails bounded");Check(handler.Requests==4,"initial plus three retry maximum");}
            using(var handler=new HttpFixture{Reply=(n,r)=>new HttpResponseMessage(HttpStatusCode.NotFound)})using(var client=new HttpClient(handler))
            {Throws(()=>DownloadTransport.Get(client,"VideoLAN.VLC","https://download.videolan.org/missing","Download",null,CancellationToken.None).GetAwaiter().GetResult(),"404 fails without retry");Check(handler.Requests==1,"permanent error not repeated");}
            using(var cancel=new CancellationTokenSource())using(var handler=new HttpFixture{Reply=(n,r)=>new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)})using(var client=new HttpClient(handler))
            {Throws(()=>DownloadTransport.Get(client,"VideoLAN.VLC","https://download.videolan.org/test","Download",null,cancel.Token,(w,t)=>{cancel.Cancel();return Task.Delay(1,t);}).GetAwaiter().GetResult(),"cancellation interrupts retry delay");Check(handler.Requests==1,"cancel does not send another request");}
            using(var handler=new HttpFixture{Reply=(n,r)=>{var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new Uri("https://attacker.invalid/setup.exe");return response;}})using(var client=new HttpClient(handler))
            {Throws(()=>DownloadTransport.Get(client,"VideoLAN.VLC","https://download.videolan.org/test","Download",null,CancellationToken.None).GetAwaiter().GetResult(),"redirect foreign origin rejected before request");Check(handler.Requests==1,"no foreign request");}
            using(var response=new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)){response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1));Throws(()=>DownloadTransport.RetryDelay(response,1),"long Retry-After fails rather than retrying too early");}
        }
    }
}
