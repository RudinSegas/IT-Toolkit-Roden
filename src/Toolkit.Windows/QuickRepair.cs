using System;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Toolkit.Core;
namespace Toolkit.Windows
{
    public static class QuickRepair
    {
        public static string ReadServiceState(string name)
        {if(!Operations.IsElevated("StartService:"+name))throw new ArgumentException("Service not allowlisted.");try{using(var service=new ManagementObject("Win32_Service.Name='"+name+"'")){service.Get();return Convert.ToString(service["State"]);}}catch{return "Unknown";}}
        public static bool HasLease(string[] addresses){return addresses!=null&&addresses.Any(x=>{System.Net.IPAddress ip;return System.Net.IPAddress.TryParse(x,out ip)&&ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&x!="0.0.0.0"&&!x.StartsWith("169.254.");});}
        public static async Task<ProcessResult> ExecuteAsync(string operation,Action<OutputChunk> output)
        {
            if(!Operations.IsElevated(operation))throw new ArgumentException("Operation is not allowlisted.");
            if(operation.StartsWith("DhcpRenew:")||operation.StartsWith("DhcpRelease:"))
            {
                var id=Guid.Parse(operation.Substring(operation.IndexOf(':')+1));bool release=operation.StartsWith("DhcpRelease:");
                using(var search=new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapterConfiguration"))using(var rows=search.Get())foreach(ManagementObject row in rows)using(row)
                {
                    Guid actual;if(!Guid.TryParse(Convert.ToString(row["SettingID"]),out actual)||actual!=id)continue;
                    if(row["DHCPEnabled"]==null||!Convert.ToBoolean(row["DHCPEnabled"]))throw new InvalidOperationException("Selected adapter is not confirmed DHCP-enabled. No static configuration changed.");
                    var before=row["IPAddress"] as string[];StateWorkflow.Emit(output,"Check: addresses "+String.Join(", ",before??new string[0]));
                    if(release&&before!=null&&!HasLease(before))return StateWorkflow.Result(ChangeVerdict.AlreadyConfigured,"No Changes Required — no usable IPv4 DHCP lease observed. No release executed.");
                    StateWorkflow.Emit(output,"Decision: explicit "+(release?"release":"renew/repair")+" requested. Execute selected adapter method.");
                    uint code=Convert.ToUInt32(row.InvokeMethod(release?"ReleaseDHCPLease":"RenewDHCPLease",null));
                    // A fresh WMI object independently queries post-state, rather than trusting the method code.
                    using(var post=new ManagementObject(row.Path.Path)){post.Get();var addresses=post["IPAddress"] as string[];
                        bool? desired=addresses==null?(release?(bool?)null:false):release?!HasLease(addresses):HasLease(addresses);
                        // Renew can retain the same valid address; without lease-time evidence it is inconclusive.
                        if(!release&&desired==true&&before!=null&&before.SequenceEqual(addresses))desired=null;
                        var verdict=StateWorkflow.Verify(code==1?3010:(int)code,new StateEvidence{Desired=desired});
                        string summary=operation+": "+verdict+"; WMI result "+code+"; after addresses: "+String.Join(", ",addresses??new string[0])+". Address availability alone does not prove internet/DNS connectivity.";return StateWorkflow.Result(verdict,summary,(int)code);
                    }
                }throw new InvalidOperationException("Selected adapter is no longer present.");
            }
            string verb=operation.Substring(0,operation.IndexOf(':')),name=operation.Substring(operation.IndexOf(':')+1);
            using(var service=new ManagementObject("Win32_Service.Name='"+name+"'"))
            {
                service.Get();string before=Convert.ToString(service["State"]);bool restart=verb=="RestartService";string desired=verb=="StopService"?"Stopped":"Running";
                StateWorkflow.Emit(output,"Check: "+name+" = "+before);
                if(StateWorkflow.Decide(new StateEvidence{Desired=before==desired},restart)==ChangeVerdict.AlreadyConfigured)return StateWorkflow.Result(ChangeVerdict.AlreadyConfigured,name+": Already "+desired+" — No Changes Required.");
                if(before!="Running"&&before!="Stopped")return StateWorkflow.Result(ChangeVerdict.VerificationInconclusive,name+": transitional/unknown state "+before+". No conflicting operation executed.");
                if(desired=="Running"&&Convert.ToString(service["StartMode"])=="Disabled")return StateWorkflow.Result(ChangeVerdict.Failed,"Service startup is Disabled; startup configuration was not changed.");
                if((restart||desired=="Stopped")&&before=="Running"&&(service["AcceptStop"]==null||!Convert.ToBoolean(service["AcceptStop"])))return StateWorkflow.Result(ChangeVerdict.Failed,"Windows reports that the service does not accept Stop; no action executed.");
                StateWorkflow.Emit(output,"Decision: "+verb+" explicitly requested. Execute.");
                if((restart||desired=="Stopped")&&before!="Stopped"){uint stop=Convert.ToUInt32(service.InvokeMethod("StopService",null));if(stop!=0)throw new InvalidOperationException("StopService result "+stop);await Wait(service,"Stopped").ConfigureAwait(false);}
                if(desired=="Running"){uint start=Convert.ToUInt32(service.InvokeMethod("StartService",null));if(start!=0&&start!=10)throw new InvalidOperationException("StartService result "+start);await Wait(service,"Running").ConfigureAwait(false);}
                using(var post=new ManagementObject(service.Path.Path)){post.Get();string after=Convert.ToString(post["State"]);var verdict=StateWorkflow.Verify(0,new StateEvidence{Desired=after==desired});string summary=name+": "+verdict+"; before "+before+"; after "+after+"; action "+verb;return StateWorkflow.Result(verdict,summary,0);}
            }
        }
        private static async Task Wait(ManagementObject service,string state){for(int i=0;i<30;i++){service.Get();if(Convert.ToString(service["State"])==state)return;await Task.Delay(1000).ConfigureAwait(false);}throw new TimeoutException("Service did not reach "+state+" in 30 seconds. Post-state remains unverified.");}
    }
}
