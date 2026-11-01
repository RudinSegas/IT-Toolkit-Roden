using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace Toolkit.Windows
{
    public static class DiagnosticMeaning
    {
        public static string Capacity(double? bytes){if(!bytes.HasValue||bytes<0)return "Not Available";return bytes>=1e12?(bytes.Value/1e12).ToString("N2",CultureInfo.InvariantCulture)+" TB":(bytes.Value/1e9).ToString("N1",CultureInfo.InvariantCulture)+" GB";}
        public static string DiskHealth(int? code){return code==0?"Healthy":code==1?"Warning":code==2?"Critical":"Unknown";}
        public static string DiskOperation(int? code){switch(code){case 0:return "Unknown";case 1:return "Other";case 2:return "OK";case 3:return "Degraded";case 4:return "Stressed";case 5:return "Predictive failure";case 6:return "Error";case 7:return "Non-recoverable error";case 8:return "Starting";case 9:return "Stopping";case 10:return "Stopped";case 11:return "In service";case 12:return "No contact";case 13:return "Lost communication";case 14:return "Aborted";case 15:return "Dormant";case 16:return "Supporting entity error";case 17:return "Completed";case 18:return "Power mode";default:return "Unknown";}}
        public static string DiskOperations(object value)
        {
            if(value==null)return "Unknown";
            var values=value as Array;var codes=values==null?new[]{value}:values.Cast<object>().ToArray();
            return codes.Length==0?"Unknown":String.Join(", ",codes.Select(x=>{int code;return Int32.TryParse(Convert.ToString(x),out code)?DiskOperation(code):"Unknown";}).Distinct());
        }
        public static string Media(int? media,int? bus){string type=media==3?"HDD":media==4?"SSD":media==5?"Storage-class memory":"Media type unknown";return bus==17?"NVMe · "+type:bus==7?"USB · "+type:bus==11?"SATA · "+type:bus==8?"RAID · "+type:type;}
        public static string Problem(int code){switch(code){case 0:return "No Device Manager problem reported";case 1:return "Device not configured correctly";case 10:return "Device cannot start";case 12:return "Insufficient resources";case 14:return "Restart required";case 18:return "Reinstall the driver";case 19:return "Registry configuration problem";case 21:return "Device removal in progress";case 22:return "Device disabled";case 24:return "Device absent or not working";case 28:return "Driver not installed";case 29:return "Device disabled by firmware";case 31:return "Windows cannot load the required driver";case 32:return "Driver service disabled";case 37:return "Driver initialization failed";case 39:return "Driver missing or damaged";case 43:return "Device reported a problem";case 45:return "Device not connected";case 48:return "Driver blocked by Windows";case 52:return "Windows cannot verify driver signature";default:return "Device Manager problem — open device properties for the Windows explanation";}}
        public static string State(string state){if(new[]{"Healthy","Critical","Action Required","Not Available"}.Contains(state))return state;if(String.IsNullOrEmpty(state)||state=="Observed"||state=="Evidence only"||state=="Event evidence"||state=="Not applicable")return "";if(state.IndexOf("denied",StringComparison.OrdinalIgnoreCase)>=0||state.IndexOf("unavailable",StringComparison.OrdinalIgnoreCase)>=0||state=="No records")return "Not Available";if(state.IndexOf("fail",StringComparison.OrdinalIgnoreCase)>=0)return "Failed";if(state=="Unknown"||state=="Not queried"||state=="Not performed"||state=="Scan not performed")return "Unknown";if(state=="Warning"||state=="Partial")return "Warning";return "";}
        public static string HumanLabel(string value){return Regex.Replace(value??"",@"(?<=[a-z])(?=[A-Z])"," ").Replace("Part Of Domain","Domain joined").Replace("Free Space","Free space").Replace("SMBIOSBIOSVersion","BIOS version").Replace("Release Date","Release date");}
    }
    public sealed class HelpdeskDiagnostics
    {
        private static object Get(Dictionary<string,object> row,string key){object value;return row.TryGetValue(key,out value)?value:null;}
        private static string S(Dictionary<string,object> row,string key){var v=Get(row,key);return v==null||String.IsNullOrWhiteSpace(Convert.ToString(v))?"Not Available":Convert.ToString(v);}
        private static double? N(Dictionary<string,object> row,string key){double n;return Double.TryParse(Convert.ToString(Get(row,key)),NumberStyles.Any,CultureInfo.InvariantCulture,out n)?(double?)n:null;}
        private static int? I(Dictionary<string,object> row,string key){var n=N(row,key);return n.HasValue?(int?)n.Value:null;}
        private static List<Dictionary<string,object>> Read(string scope,string query,DiagnosticSnapshot s,CancellationToken token)
        {
            var result=new List<Dictionary<string,object>>();
            using(var search=new ManagementObjectSearcher(scope,query,new EnumerationOptions{Timeout=TimeSpan.FromSeconds(8),ReturnImmediately=false}))using(var rows=search.Get())foreach(ManagementObject row in rows)using(row)
            {token.ThrowIfCancellationRequested();if(result.Count>=2000)throw new InvalidOperationException("Provider result limit reached; evidence incomplete.");var d=new Dictionary<string,object>();foreach(PropertyData p in row.Properties)d[p.Name]=p.Value;result.Add(d);s.TechnicalRecords.Add(new DiagnosticRecord{Name=query,Value=String.Join("; ",d.Select(x=>x.Key+"="+(x.Value is Array?String.Join(",",((Array)x.Value).Cast<object>()):Convert.ToString(x.Value)))),State=""});}return result;
        }
        public async Task<DiagnosticSnapshot> CollectAsync(string section,string adapter,bool problems,CancellationToken token)
        {
            if(section=="Storage Health")return Storage(token);
            if(section=="BIOS & Hardware")return Hardware(token);
            if(section=="Devices & Drivers")return Devices(problems,token);
            if(section=="Network Diagnostics+"){var network=Network(token);var evidence=await new ExtendedDiagnostics().CollectAsync(section,adapter,problems,token).ConfigureAwait(false);network.TechnicalRecords.AddRange(evidence.Records);return network;}
            var raw=await new ExtendedDiagnostics().CollectAsync(section,adapter,problems,token).ConfigureAwait(false);
            if(section=="Domain Diagnostics")return DomainSummary(raw);
            var s=new DiagnosticSnapshot{Section=section,Timestamp=raw.Timestamp};s.TechnicalRecords.AddRange(raw.Records);
            foreach(var row in raw.Records)
            {
                if(section=="Windows Update Diagnostics"&&(row.Name.StartsWith("Update history item")||row.Name.StartsWith("Update event")))continue;
                if(section=="Performance"&&!Regex.IsMatch(row.Name,"(?i)Top CPU|Memory utilization|Available RAM|LoadPercentage|Startup|Command|Location"))continue;
                if(section=="Domain Diagnostics"&&Regex.IsMatch(row.Name,"(?i)ticket cache|recent processing|applied user policy")){s.Add(DiagnosticMeaning.HumanLabel(row.Name),row.State=="Query returned evidence"?"Query completed — expand technical details to view results":row.State,DiagnosticMeaning.State(row.State));continue;}
                if(row.Name.Contains("SELECT ")){s.Add("Provider information",row.Value,"Not Available");continue;}
                if(row.Name.Contains("code legend")||row.Name.Contains("interpretation"))continue;
                string name=DiagnosticMeaning.HumanLabel(row.Name),value=row.Value,state=DiagnosticMeaning.State(row.State);
                if(row.Name.EndsWith("PartOfDomain"))value=row.Value=="True"?"Yes":row.Value=="False"?"No":"Unknown";
                if((row.Name.EndsWith("ReleaseDate")||row.Name.EndsWith("LastBootUpTime"))&&row.Value.Length>=8){try{value=ManagementDateTimeConverter.ToDateTime(row.Value).ToString("yyyy-MM-dd");}catch{value="Not Available";}}
                if(row.Name.EndsWith("TotalPhysicalMemory")){double bytes;if(Double.TryParse(value,out bytes))value=DiagnosticMeaning.Capacity(bytes);}
                if(row.Name.EndsWith("LoadPercentage")&&value!="Unavailable")value+="%";
                if(section=="Pending Reboot"){
                    name=row.Name.Contains("Component Based Servicing")?"Windows component servicing":row.Name.Contains("WindowsUpdate")?"Windows Update":row.Name;
                    if(row.Name=="Reboot state"){value=value=="No Known Reboot Pending"?"No Restart Detected":value=="Reboot Required"?"Restart Required":"Unknown";state=value=="Restart Required"?"Action Required":value=="Unknown"?"Unknown":"";s.Summary=value;}
                    else if(value=="Present")state="Action Required";
                }
                if(section=="Domain Diagnostics"&&row.Name.Contains("secure channel")){value=row.State=="Query returned evidence"?"Native query completed; trust status is in technical details":value;state=row.State=="Query returned evidence"?"Unknown":state;}
                s.Add(name,value,state);
            }
            if(section=="Windows Update Diagnostics"){
                var errors=raw.Records.Where(r=>r.Name.StartsWith("Update event")).ToArray();s.Add("Recent update warnings / errors",errors.Length==0?"No entries returned from the last 7 days; provider failures are reported separately.":errors.Length+" events in the last 7 days (query capped at 100). Inspect Event Logs → WindowsUpdateClient for details.",errors.Length>0?"Warning":"");
                s.Add("Recommended action",errors.Length>0?"Review the latest Windows Update error, check restart indicators, then use Windows Update Settings. Do not reset update services blindly.":"Use Windows Update Settings to manage updates; run the optional scan only when availability needs checking.");
            }
            if(String.IsNullOrEmpty(s.Summary))s.Summary=Summary(s);
            return s;
        }
        public static DiagnosticSnapshot DomainSummary(DiagnosticSnapshot raw)
        {
            var s=new DiagnosticSnapshot{Section=raw.Section,Timestamp=raw.Timestamp};s.TechnicalRecords.AddRange(raw.Records);
            foreach(var row in raw.Records.Where(r=>r.Name.StartsWith("Computer identity / registration · "))){string name=row.Name.Substring("Computer identity / registration · ".Length);if(!new[]{"Computer","Domain / workgroup","Domain joined","Entra joined","Workplace registered"}.Contains(name))continue;s.Add(name,row.Value=="True"?"Yes":row.Value=="False"?"No":row.Value,row.Value=="Unavailable"?"Unknown":"");}
            var dc=raw.Records.FirstOrDefault(r=>r.Name=="DC discovery / connectivity");
            if(dc!=null){var match=Regex.Match(dc.Value,@"(?m)^Controller: ([^\r\n]+)");s.Add("Detected domain controller",match.Success?match.Groups[1].Value:dc.Value,match.Success?"":dc.State=="Not applicable"?"":"Unknown");s.Add("Domain controller connectivity",dc.State=="Success"?"LDAP TCP port 389 reachable — this does not prove authentication or trust.":dc.State=="Not applicable"?"Not applicable — not AD joined":"LDAP port could not be verified. Check VPN, routing and firewall.",dc.State=="Success"?"":dc.State=="Not applicable"?"":"Warning");}
            var dns=raw.Records.FirstOrDefault(r=>r.Name=="Domain DNS");if(dns!=null)s.Add("Domain DNS resolution",dns.State=="Resolved"?"Resolved by the Windows DNS resolver. Addresses are in technical evidence.":dns.Value,dns.State=="Resolved"?"":"Warning");
            var trust=raw.Records.FirstOrDefault(r=>r.Name=="Authentication / secure channel");
            if(trust!=null){bool healthy=trust.State=="Query returned evidence"&&Regex.IsMatch(trust.Value,@"(?im)(?:=|:)\s*0\s+0x0\s+NERR_Success\s*$");s.Add("Secure channel",healthy?"Healthy — existing Netlogon trusted connection reports success; not a new interactive authentication test.":trust.State=="Not applicable"?trust.Value:"Unknown — a successful trusted-connection status could not be established. Inspect the native result in technical evidence.",healthy?"Healthy":trust.State=="Not applicable"?"":"Unknown");}
            var gp=raw.Records.FirstOrDefault(r=>r.Name=="Group Policy / applied user policy");if(gp!=null)s.Add("Applied user Group Policy",gp.State=="Query returned evidence"?"GPResult returned applied-policy information; expand technical evidence for policy names.":"Not Available — user GPResult could not return verified policy data.",gp.State=="Query returned evidence"?"":"Not Available");
            var events=raw.Records.FirstOrDefault(r=>r.Name=="Group Policy / recent processing and warnings");if(events!=null){int warnings=Regex.Matches(events.Value,@"Level=[123]\b").Count;s.Add("Recent Group Policy warnings",warnings>0?warnings+" warning/error event(s) in the bounded recent query. Review Event Logs → GroupPolicy/Operational.":events.Value.StartsWith("Group Policy evidence unavailable")?"Event evidence unavailable.":"No warning/error severity entries returned by the bounded recent query; this does not verify every extension.",warnings>0?"Warning":events.Value.StartsWith("Group Policy evidence unavailable")?"Not Available":"");}
            s.Summary=Summary(s);return s;
        }
        public static string Summary(DiagnosticSnapshot s)
        {
            int issues=s.Records.Count(r=>new[]{"Warning","Critical","Failed","Action Required"}.Contains(r.State));int unknown=s.Records.Count(r=>r.State=="Unknown"||r.State=="Not Available");
            return issues>0?issues+" item(s) need attention. Review highlighted results below.":unknown>0?"Some information could not be verified ("+unknown+" item(s)). Review unavailable checks below.":"Collection complete. Review the category results below; ordinary values are not health certifications.";
        }
        private static DiagnosticSnapshot Hardware(CancellationToken token)
        {
            var s=new DiagnosticSnapshot{Section="BIOS & Hardware"};
            try{foreach(var r in Read(@"root\cimv2","SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS",s,token)){s.Add("BIOS vendor",S(r,"Manufacturer"));s.Add("BIOS version",S(r,"SMBIOSBIOSVersion"));string date=S(r,"ReleaseDate");try{date=ManagementDateTimeConverter.ToDateTime(date).ToString("yyyy-MM-dd");}catch{date="Not Available";}s.Add("BIOS release date",date);}
                foreach(var r in Read(@"root\cimv2","SELECT Manufacturer,Model,TotalPhysicalMemory FROM Win32_ComputerSystem",s,token)){s.Add("Computer manufacturer",S(r,"Manufacturer"));s.Add("Computer model",S(r,"Model"));s.Add("Installed memory",DiagnosticMeaning.Capacity(N(r,"TotalPhysicalMemory")));}
                var drivers=Read(@"root\cimv2","SELECT DeviceName,DriverVersion,DriverDate,DriverProviderName,IsSigned FROM Win32_PnPSignedDriver",s,token);int unsigned=drivers.Count(r=>Get(r,"IsSigned")!=null&&!Convert.ToBoolean(Get(r,"IsSigned")));s.Add("Driver inventory",drivers.Count+" records. Complete device/provider/version/date evidence is under Technical evidence.");if(unsigned>0)s.Add("Driver signatures",unsigned+" record(s) report IsSigned=False. Inspect the driver provider before taking action.","Warning");
                s.Summary="BIOS and computer identity collected. Driver versions are included in the optional technical evidence; no firmware changes performed.";
            }catch(OperationCanceledException){throw;}catch(Exception e){s.Add("Hardware provider",e.Message,"Not Available");s.Summary="Some BIOS/driver information is unavailable.";}return s;
        }
        private static DiagnosticSnapshot Devices(bool problems,CancellationToken token)
        {
            var s=new DiagnosticSnapshot{Section="Devices & Drivers"};
            try{var rows=Read(@"root\cimv2","SELECT Name,DeviceID,ConfigManagerErrorCode,Status,Manufacturer FROM Win32_PnPEntity",s,token);int count=rows.Count(x=>I(x,"ConfigManagerErrorCode").HasValue&&I(x,"ConfigManagerErrorCode")!=0);int unknown=rows.Count(x=>!I(x,"ConfigManagerErrorCode").HasValue);
                s.Summary=rows.Count==0?"No device records available; device health is Unknown.":count>0?count+" device(s) report problems. Open Device Manager to inspect or update the affected drivers.":unknown>0?"No errors in readable devices; "+unknown+" device(s) have unknown status.":"No Device Manager problems detected in "+rows.Count+" returned devices.";
                foreach(var row in rows){int? code=I(row,"ConfigManagerErrorCode");if(problems&&code==0)continue;s.Add(S(row,"Name"),code.HasValue?"Code "+code+" — "+DiagnosticMeaning.Problem(code.Value):"Problem status unavailable",code==0?"":code.HasValue?"Action Required":"Unknown");}
            }catch(OperationCanceledException){throw;}catch(Exception e){s.Summary="Device inventory unavailable.";s.Add("Device query",e.Message,"Not Available");}return s;
        }
        private static DiagnosticSnapshot Network(CancellationToken token)
        {
            var s=new DiagnosticSnapshot{Section="Network Diagnostics+"};int active=0;
            foreach(var n in NetworkInterface.GetAllNetworkInterfaces()){
                token.ThrowIfCancellationRequested();if(n.NetworkInterfaceType==NetworkInterfaceType.Loopback)continue;if(n.OperationalStatus!=OperationalStatus.Up)continue;active++;
                try{var p=n.GetIPProperties();string name=n.Name;s.Add(name+" · Adapter",n.Description);s.Add(name+" · IP addresses",String.Join(", ",p.UnicastAddresses.Select(x=>x.Address)));s.Add(name+" · Gateway",p.GatewayAddresses.Count==0?"None configured":String.Join(", ",p.GatewayAddresses.Select(x=>x.Address)),p.GatewayAddresses.Count==0?"Warning":"");s.Add(name+" · DNS servers",p.DnsAddresses.Count==0?"None configured":String.Join(", ",p.DnsAddresses),p.DnsAddresses.Count==0?"Warning":"");var v4=p.GetIPv4Properties();s.Add(name+" · DHCP",v4==null?"Not Available":v4.IsDhcpEnabled?"Enabled":"Disabled / static configuration");if(p.UnicastAddresses.Any(x=>x.Address.ToString().StartsWith("169.254.")))s.Add(name+" · Address warning","Link-local IPv4 address detected; check DHCP/server connectivity.","Warning");}
                catch(Exception e){s.Add(n.Name,e.Message,"Not Available");}}
            s.Summary=active==0?"No active non-loopback adapters detected. Check cable, Wi-Fi and adapter state.":active+" active adapter(s). Use Network Diagnostics for targeted Ping/DNS/TCP tests.";return s;
        }
        private static DiagnosticSnapshot Storage(CancellationToken token)
        {
            var s=new DiagnosticSnapshot{Section="Storage Health"};const string ns=@"root\Microsoft\Windows\Storage";var physical=new List<Dictionary<string,object>>();var reliability=new List<Dictionary<string,object>>();
            try{physical=Read(ns,"SELECT * FROM MSFT_PhysicalDisk",s,token);}catch(Exception e){s.TechnicalRecords.Add(new DiagnosticRecord{Name="Physical media provider",Value=e.Message,State="Not Available"});}
            try{reliability=Read(ns,"SELECT * FROM MSFT_StorageReliabilityCounter",s,token);}catch(Exception e){s.TechnicalRecords.Add(new DiagnosticRecord{Name="Reliability provider",Value=e.Message,State="Not Available"});}
            int disks=0;
            try{foreach(var row in Read(ns,"SELECT * FROM MSFT_Disk",s,token)){
                disks++;string number=S(row,"Number"),title="Disk "+number+" — "+S(row,"FriendlyName");var matches=physical.Where(x=>S(x,"UniqueId")!="Not Available"&&S(x,"UniqueId")==S(row,"UniqueId")).ToArray();var media=matches.Length==1?I(matches[0],"MediaType"):null;
                string health=DiagnosticMeaning.DiskHealth(I(row,"HealthStatus"));s.Add(title+" · Health",health=="Unknown"?"Unknown — Detailed disk health data unavailable.":health+" (Windows Storage provider; not a full SMART self-test)",health);
                s.Add(title+" · Device",S(row,"Manufacturer")+" · "+S(row,"Model")+" · "+DiagnosticMeaning.Media(media,I(row,"BusType")));
                s.Add(title+" · Capacity",DiagnosticMeaning.Capacity(N(row,"Size")));s.Add(title+" · Operational status",(Get(row,"IsOffline")==null?"":Convert.ToBoolean(Get(row,"IsOffline"))?"Offline · ":"Online · ")+DiagnosticMeaning.DiskOperations(Get(row,"OperationalStatus")));
                var counters=reliability.Where(x=>S(x,"DeviceId")==number).ToArray();var counter=counters.Length==1?counters[0]:null;
                s.Add(title+" · Temperature",counter==null||!N(counter,"Temperature").HasValue?"Not Available":N(counter,"Temperature")+" °C");
                if(counter==null)s.Add(title+" · SMART / reliability","Detailed reliability counters unavailable; full SMART health Unknown.","Unknown");
                else {foreach(var key in new[]{"ReadErrorsUncorrected","WriteErrorsUncorrected","Wear"}){var value=N(counter,key);s.Add(title+" · "+DiagnosticMeaning.HumanLabel(key),value.HasValue?value+(key=="Wear"?"% of estimated wear limit":""):"Not Available",!value.HasValue?"Unknown":value>(key=="Wear"?99:0)?"Warning":"");}}
            }}catch(OperationCanceledException){throw;}catch(Exception e){s.Add("Disk health provider",e.Message,"Not Available");}
            if(disks==0){try{foreach(var row in Read(@"root\cimv2","SELECT Index,Model,Size FROM Win32_DiskDrive",s,token)){s.Add("Disk "+S(row,"Index")+" — "+S(row,"Model"),DiagnosticMeaning.Capacity(N(row,"Size"))+" · Health: Unknown — Detailed disk health data unavailable.","Unknown");}}catch(Exception e){s.Add("Physical disks",e.Message,"Not Available");}}
            var bitlocker=new Dictionary<string,string>();try{foreach(var row in Read(@"root\CIMV2\Security\MicrosoftVolumeEncryption","SELECT DriveLetter,ProtectionStatus FROM Win32_EncryptableVolume",s,token))bitlocker[S(row,"DriveLetter")]=I(row,"ProtectionStatus")==1?"Protection on":I(row,"ProtectionStatus")==0?"Protection off":"Unknown";}catch(Exception e){s.TechnicalRecords.Add(new DiagnosticRecord{Name="BitLocker provider",Value=e.Message,State="Not Available"});}
            foreach(var drive in DriveInfo.GetDrives().Where(x=>x.DriveType==DriveType.Fixed))try{double used=drive.TotalSize-drive.TotalFreeSpace,percent=drive.TotalSize>0?used/drive.TotalSize*100:0;string protection;bitlocker.TryGetValue(drive.Name.TrimEnd('\\'),out protection);
                s.Add(drive.Name+" Volume", "Total: "+DiagnosticMeaning.Capacity(drive.TotalSize)+" · Free: "+DiagnosticMeaning.Capacity(drive.TotalFreeSpace)+" · Used: "+DiagnosticMeaning.Capacity(used)+" ("+percent.ToString("N1")+"%)\r\nFile system: "+drive.DriveFormat+" · BitLocker: "+(protection??"Not Available"),percent>=95?"Critical":percent>=90?"Warning":"");
            }catch(Exception e){s.Add(drive.Name,e.Message,"Not Available");}
            try{Read(@"root\cimv2","SELECT DeviceID,DiskIndex,Size,Type FROM Win32_DiskPartition",s,token);}catch(OperationCanceledException){throw;}catch(Exception e){s.TechnicalRecords.Add(new DiagnosticRecord{Name="Partition provider",Value=e.Message,State="Not Available"});}
            try{foreach(var row in Read(@"root\wmi","SELECT InstanceName,PredictFailure,Reason FROM MSStorageDriver_FailurePredictStatus",s,token))if(Get(row,"PredictFailure")!=null&&Convert.ToBoolean(Get(row,"PredictFailure")))s.Add("SMART predictive failure",S(row,"InstanceName")+" reports PredictFailure=True. Back up data and inspect this physical device. Reason: "+S(row,"Reason"),"Critical");}catch(OperationCanceledException){throw;}catch(Exception e){s.TechnicalRecords.Add(new DiagnosticRecord{Name="Legacy SMART provider",Value=e.Message,State="Not Available"});}
            s.Summary=Summary(s)+" Capacity uses decimal GB/TB; low-space warnings start at 90% used (critical at 95%).";return s;
        }
    }
}
