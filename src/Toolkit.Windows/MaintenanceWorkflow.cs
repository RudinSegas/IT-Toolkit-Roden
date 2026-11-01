using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Toolkit.Core;
namespace Toolkit.Windows
{
    public static class MaintenanceWorkflow
    {
        public static string ParseFeature(string text)
        {
            var match=Regex.Match(text??"",@"(?im)^\s*State\s*:\s*(Enabled|Disabled with Payload Removed|Disabled|Enable Pending|Disable Pending)\s*$");
            return match.Success?match.Groups[1].Value.StartsWith("Disabled")?"Disabled":match.Groups[1].Value:"Unknown";
        }
        public static string ReadFeatureWithoutElevation()
        {
            try{
                var reboot=RebootDetection.Collect().Records.Last().Value;
                if(reboot!="No Known Reboot Pending")return "Unknown";
                using(var q=new ManagementObjectSearcher("SELECT InstallState FROM Win32_OptionalFeature WHERE Name='NetFx3'"))using(var rows=q.Get())foreach(ManagementObject row in rows)using(row){int state=Convert.ToInt32(row["InstallState"]);return state==1?"Enabled":state==2||state==3?"Disabled":"Unknown";}
            }catch{}return "Unknown";
        }
        private static async Task<string> Feature(Action<OutputChunk> output,CancellationToken token)
        {
            var text=new StringBuilder();var r=await new ProcessEngine().RunAsync(Path.Combine(Environment.SystemDirectory,"dism.exe"),new[]{"/English","/Online","/Get-FeatureInfo","/FeatureName:NetFx3"},Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),c=>{text.Append(c.Text);if(output!=null)output(c);},token,true,60,true).ConfigureAwait(false);
            return r.ExitCode==0?ParseFeature(text.ToString()):"Unknown";
        }
        public static ChangeVerdict FeatureDecision(string state)
        {return StateWorkflow.Decide(new StateEvidence{Desired=state=="Enabled"?(bool?)true:state=="Disabled"?(bool?)false:null,PendingRestart=state=="Enable Pending"||state=="Disable Pending"},false);}
        public static async Task<ProcessResult> ExecuteAsync(string operation,Action<OutputChunk> output,CancellationToken token)
        {
            string before="Not applicable",after="Unknown";
            if(operation=="NetFx3")
            {
                before=await Feature(output,token).ConfigureAwait(false);StateWorkflow.Emit(output,"Check: .NET Framework 3.5 = "+before);
                var decision=FeatureDecision(before);
                if(decision==ChangeVerdict.AlreadyConfigured)return StateWorkflow.Result(decision,".NET Framework 3.5: Already Enabled — No Action Required. No changes were made.");
                if(decision==ChangeVerdict.RestartRequired)return StateWorkflow.Result(decision,".NET Framework 3.5: "+before+" — Restart Required; no repeated DISM enablement.");
                if(before=="Unknown")return StateWorkflow.Result(ChangeVerdict.VerificationInconclusive,".NET Framework 3.5: current state Unknown. No enablement executed. Check DISM access/source diagnostics above.");
            }
            else if(operation=="DnsFlush")
            {before=await Capture("ipconfig.exe",new[]{"/displaydns"},output,token).ConfigureAwait(false);}
            else if(operation=="Sfc"||operation.StartsWith("Dism"))
            {before=RebootDetection.Collect().ToString();StateWorkflow.Emit(output,"Check: "+before+"\r\nDecision: explicit diagnostic/repair requested. An unproven healthy state will not suppress the requested scan.");}
            StateWorkflow.Emit(output,"Execute: "+operation);
            var command=Operations.Command(operation);
            var result=await new ProcessEngine().RunAsync(Path.Combine(Environment.SystemDirectory,command[0]),command.Skip(1),operation=="Sfc"?Encoding.Unicode:Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),output,token,restrictedEnvironment:true).ConfigureAwait(false);
            if(operation=="NetFx3")after=await Feature(output,CancellationToken.None).ConfigureAwait(false);
            else if(operation=="DnsFlush")after=await Capture("ipconfig.exe",new[]{"/displaydns"},output,CancellationToken.None).ConfigureAwait(false);
            else if(operation=="DismRestore")after=await Capture("dism.exe",new[]{"/English","/Online","/Cleanup-Image","/CheckHealth"},output,CancellationToken.None).ConfigureAwait(false);
            else if(operation=="Sfc")after="Independent repair verification not established. Review current CBS log and Windows health evidence; exit zero alone is not verification.";
            else after="Diagnostic output collected; this action does not itself establish a changed configuration.";
            bool? desired=operation=="NetFx3"?(after=="Enabled"?(bool?)true:after=="Disabled"?(bool?)false:null):null;
            var verdict=StateWorkflow.Verify(result.ExitCode,new StateEvidence{Desired=desired,PendingRestart=after=="Enable Pending"||after=="Disable Pending"});
            string summary=operation+": "+verdict+"\r\nBefore: "+before+"\r\nNative exit: "+result.ExitCode+"\r\nAfter: "+after;
            if(operation=="NetFx3"&&verdict==ChangeVerdict.Failed)summary+="\r\nReview DISM source acquisition errors, configured Windows Update/WSUS restrictions and CBS/DISM logs. No source or policy bypass was attempted.";
            return StateWorkflow.Result(verdict,summary,result.ExitCode);
        }
        private static async Task<string> Capture(string exe,string[] args,Action<OutputChunk> output,CancellationToken token)
        {var buffer=new TerminalBuffer();var r=await new ProcessEngine().RunAsync(Path.Combine(Environment.SystemDirectory,exe),args,Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),c=>{buffer.Append(c.Text);if(output!=null)output(c);},token,true,60,true).ConfigureAwait(false);return "Diagnostic exit "+r.ExitCode+"\r\n"+buffer;}
    }
}
