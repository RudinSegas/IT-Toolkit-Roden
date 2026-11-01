using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Toolkit.Windows
{
    public sealed class DiagnosticRecord
    {
        public string Section { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
        public string State { get; set; }
        public string Timestamp { get; set; }
        public DiagnosticRecord() { Timestamp = DateTimeOffset.Now.ToString("o"); State = ""; }
    }
    public sealed class DiagnosticSnapshot
    {
        public string Section { get; set; }
        public string Timestamp { get; set; }
        public List<DiagnosticRecord> Records { get; set; }
        public string Summary { get; set; }
        public List<DiagnosticRecord> TechnicalRecords { get; set; }
        public DiagnosticSnapshot() { Timestamp = DateTimeOffset.Now.ToString("o"); Records = new List<DiagnosticRecord>();TechnicalRecords=new List<DiagnosticRecord>(); }
        public void Add(string name, object value, string state = "") { Records.Add(new DiagnosticRecord { Section = Section, Name = name, Value = Convert.ToString(value, CultureInfo.InvariantCulture), State = state }); }
        public override string ToString() { return Section + " · " + Timestamp + "\r\n" + String.Join("\r\n", Records.Select(x => x.Name + ": " + x.Value + " [" + x.State + "]")); }
        public static string Compare(DiagnosticSnapshot before, DiagnosticSnapshot after)
        {
            if (before == null || after == null || before.Section != after.Section) return "Two snapshots of the same section are required.";
            var a = before.Records.GroupBy(x => x.Name).ToDictionary(g => g.Key, g => String.Join(" | ", g.Select(x => x.Value + " [" + x.State + "]")));
            var b = after.Records.GroupBy(x => x.Name).ToDictionary(g => g.Key, g => String.Join(" | ", g.Select(x => x.Value + " [" + x.State + "]")));
            var lines = a.Keys.Union(b.Keys).Where(k => !a.ContainsKey(k) || !b.ContainsKey(k) || a[k] != b[k]).Select(k => k + ": " + (a.ContainsKey(k) ? a[k] : "<absent>") + " → " + (b.ContainsKey(k) ? b[k] : "<absent>"));
            return "Compared " + before.Timestamp + " → " + after.Timestamp + "\r\n" + (lines.Any() ? String.Join("\r\n", lines) : "No observed changes.");
        }
    }
    public static class RebootDetection
    {
        public static string Classify(IEnumerable<bool?> indicators) { var items = indicators.ToArray(); return items.Any(x => x == true) ? "Reboot Required" : items.Length == 0 || items.Any(x => !x.HasValue) ? "Unknown" : "No Known Reboot Pending"; }
        public static DiagnosticSnapshot Collect()
        {
            var s = new DiagnosticSnapshot { Section = "Pending Reboot" }; var flags = new List<bool?>();
            foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending", @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired" })
            { try { using (var key = Registry.LocalMachine.OpenSubKey(path)) { flags.Add(key != null); s.Add(path, key != null ? "Present" : "Not present"); } } catch (Exception e) { flags.Add(null); s.Add(path, e.Message, "Unknown"); } }
            try { using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager")) { var values = key == null ? null : key.GetValue("PendingFileRenameOperations") as string[]; flags.Add(key == null ? (bool?)null : values != null && values.Length > 0); s.Add("Pending file rename operations", key == null ? "Unavailable" : values == null ? "None observed" : values.Length + " entries (paths omitted)"); } } catch (Exception e) { flags.Add(null); s.Add("Pending file rename", e.Message, "Unknown"); }
            s.Add("Reboot state", Classify(flags)); return s;
        }
    }
    public sealed class ExtendedDiagnostics
    {
        public static readonly string[] Sections = { "Overview", "BIOS & Hardware", "Network Diagnostics+", "Windows Update Diagnostics", "Pending Reboot", "Storage Health", "Devices & Drivers", "Installed Software", "Performance", "Domain Diagnostics" };
        public static string[] Adapters() { return new[] { "All adapters" }.Concat(NetworkInterface.GetAllNetworkInterfaces().Select(n => n.Id + " | " + n.Name)).ToArray(); }
        private static bool Match(NetworkInterface n, string selected) { return String.IsNullOrEmpty(selected) || selected == "All adapters" || selected.StartsWith(n.Id + " | ", StringComparison.Ordinal); }
        public static void Query(DiagnosticSnapshot s, string scope, string query, string[] columns, CancellationToken token)
        {
            try
            {
                using (var search = new ManagementObjectSearcher(scope, query, new EnumerationOptions { Timeout = TimeSpan.FromSeconds(8), ReturnImmediately = false }))
                using (var rows = search.Get())
                { int count = 0; foreach (ManagementObject row in rows) using (row) { token.ThrowIfCancellationRequested(); if (++count > 1500) { s.Add(query, "Record limit 1500", "Partial"); break; } string label = Convert.ToString(row[columns[0]]); foreach (string column in columns) { object v; try { v = row[column]; } catch { v = null; } var array = v as Array; s.Add(label + " · " + column, v == null ? "Unavailable" : array == null ? Convert.ToString(v) : String.Join(", ", array.Cast<object>()), v == null ? "Unknown" : "Observed"); } } if (count == 0) s.Add(query, "No returned records; does not establish healthy/supported", "No records"); }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { s.Add(query, e.Message, e is UnauthorizedAccessException ? "Access denied" : "Unavailable"); }
        }
        public async Task<DiagnosticSnapshot> CollectAsync(string section, string adapter, bool problemOnly, CancellationToken token)
        {
            var s = new DiagnosticSnapshot { Section = section };
            switch (section)
            {
                case "Overview":
                    s.Records.AddRange(RebootDetection.Collect().Records);
                    Query(s,@"root\cimv2","SELECT Caption,Version,LastBootUpTime FROM Win32_OperatingSystem",new[]{"Caption","Version","LastBootUpTime"},token);
                    foreach(var drive in DriveInfo.GetDrives().Where(x=>x.DriveType==DriveType.Fixed))try{s.Add(drive.Name+" · Available space",(drive.AvailableFreeSpace/1073741824.0).ToString("N1")+" GiB / "+(drive.TotalSize/1073741824.0).ToString("N1")+" GiB");}catch(Exception e){s.Add(drive.Name,e.Message,"Unknown");}
                    s.Add("Coverage","Choose a category to collect its evidence. No uncollected category is assumed healthy.","On demand");break;
                case "BIOS & Hardware":
                    Query(s,@"root\cimv2","SELECT * FROM Win32_BIOS",new[]{"Manufacturer","SMBIOSBIOSVersion","ReleaseDate","SerialNumber"},token);
                    Query(s,@"root\cimv2","SELECT * FROM Win32_ComputerSystem",new[]{"Manufacturer","Model","TotalPhysicalMemory"},token);
                    Query(s,@"root\cimv2","SELECT * FROM Win32_Processor",new[]{"Name","NumberOfCores","NumberOfLogicalProcessors"},token);break;
                case "Pending Reboot": return RebootDetection.Collect();
                case "Network Diagnostics+":
                    foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => Match(n, adapter)))
                    {
                        token.ThrowIfCancellationRequested(); var p = n.GetIPProperties(); s.Add(n.Name + " · State", n.OperationalStatus); s.Add(n.Name + " · Description", n.Description); s.Add(n.Name + " · MAC", n.GetPhysicalAddress());
                        s.Add(n.Name + " · Addresses / prefix", String.Join(", ", p.UnicastAddresses.Select(x => x.Address + "/" + x.PrefixLength))); s.Add(n.Name + " · Gateway", String.Join(", ", p.GatewayAddresses.Select(x => x.Address))); s.Add(n.Name + " · DNS", String.Join(", ", p.DnsAddresses)); s.Add(n.Name + " · DHCP servers", String.Join(", ", p.DhcpServerAddresses));
                    }
                    Query(s, @"root\cimv2", "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=TRUE", new[] { "Description", "SettingID", "DHCPEnabled", "DHCPLeaseObtained", "DHCPLeaseExpires", "IPSubnet" }, token);
                    Query(s, @"root\StandardCimv2", "SELECT * FROM MSFT_NetRoute", new[] { "InterfaceIndex", "DestinationPrefix", "NextHop", "RouteMetric", "AddressFamily" }, token); break;
                case "Storage Health":
                    Query(s, @"root\cimv2", "SELECT * FROM Win32_DiskDrive", new[] { "DeviceID", "Model", "Size", "InterfaceType", "Status" }, token);
                    Query(s, @"root\cimv2", "SELECT * FROM Win32_DiskPartition", new[] { "DeviceID", "DiskIndex", "Size", "Type" }, token);
                    Query(s, @"root\cimv2", "SELECT * FROM Win32_LogicalDisk WHERE DriveType=3", new[] { "DeviceID", "Size", "FreeSpace", "FileSystem" }, token);
                    Query(s, @"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk", new[] { "FriendlyName", "MediaType", "BusType", "HealthStatus", "OperationalStatus", "Size" }, token);
                    foreach(var drive in DriveInfo.GetDrives().Where(x=>x.DriveType==DriveType.Fixed))try{s.Add(drive.Name+" · Used bytes",drive.TotalSize-drive.TotalFreeSpace);}catch(Exception e){s.Add(drive.Name,e.Message,"Unavailable");}
                    s.Add("Storage code legend", "MediaType: 0 unspecified, 3 HDD, 4 SSD, 5 SCM. BusType: 17 NVMe. HealthStatus: 0 Healthy, 1 Warning, 2 Unhealthy, 5 Unknown. Unsupported data is not healthy.");
                    Query(s, @"root\wmi", "SELECT * FROM MSStorageDriver_FailurePredictStatus", new[] { "InstanceName", "PredictFailure", "Reason" }, token);
                    BitLocker(s,token);
                    s.Add("BitLocker legend", "Protection: 0 off, 1 on, 2 unknown. Conversion: 0 decrypted, 1 encrypted, 2 encrypting, 3 decrypting, 4/5 paused. No recovery keys queried."); break;
                case "Devices & Drivers":
                    Query(s, @"root\cimv2", "SELECT * FROM Win32_PnPEntity" + (problemOnly ? " WHERE ConfigManagerErrorCode<>0" : ""), new[] { "Name", "DeviceID", "Manufacturer", "Status", "ConfigManagerErrorCode" }, token);
                    s.Add("Problem code legend", "0 no reported error; 10 cannot start; 22 disabled; 28 drivers not installed; 43 device reported a problem. Full codes: Microsoft Device Manager.");
                    Query(s, @"root\cimv2", "SELECT * FROM Win32_PnPSignedDriver", new[] { "DeviceName", "DeviceID", "DriverProviderName", "DriverVersion", "DriverDate", "IsSigned" }, token);
                    Query(s, @"root\cimv2", "SELECT * FROM Win32_BIOS", new[] { "Manufacturer", "SMBIOSBIOSVersion", "ReleaseDate" }, token);
                    Query(s, @"root\cimv2", "SELECT * FROM Win32_ComputerSystem", new[] { "Manufacturer", "Model" }, token); break;
                case "Windows Update Diagnostics":
                    Query(s, @"root\cimv2", "SELECT * FROM Win32_Service WHERE Name='wuauserv' OR Name='BITS' OR Name='UsoSvc'", new[] { "Name", "State", "StartMode" }, token);
                    Query(s, @"root\cimv2", "SELECT * FROM Win32_OperatingSystem", new[] { "Caption", "Version", "BuildNumber" }, token);
                    s.Records.AddRange(RebootDetection.Collect().Records);
                    UpdateHistory(s); UpdateEvents(s); s.Add("Available updates", "Scan not performed. Use Scan available updates explicitly; Windows policy remains authoritative.", "Scan not performed");
                    try { using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Detect")) s.Add("Last scan (legacy registry evidence)", key == null ? "Unavailable" : key.GetValue("LastSuccessTime", "Unavailable"), "Evidence only"); } catch (Exception e) { s.Add("Last scan", e.Message, "Unavailable"); } break;
                case "Performance": await Performance(s, token).ConfigureAwait(false); break;
                case "Domain Diagnostics":
                    Query(s, @"root\cimv2", "SELECT * FROM Win32_ComputerSystem", new[] { "Name", "PartOfDomain", "Domain" }, token);
                    var d = await new NetworkDiagnostics().RunAsync("Domain", "", 389, 3000, token).ConfigureAwait(false); s.Add("DC discovery / connectivity", d.Text, d.Status);
                    string domain = null;
                    try { domain = IPGlobalProperties.GetIPGlobalProperties().DomainName; } catch { }
                    if (!String.IsNullOrEmpty(domain)) { var dns = await new NetworkDiagnostics().RunAsync("Dns", domain, 53, 3000, token).ConfigureAwait(false); s.Add("Domain DNS", dns.Text, dns.Status); }
                    foreach(var field in WindowsInventoryService.Identity().Fields)s.Add("Computer identity / registration · "+field.Name,field.Value);
                    await DomainEvidence(s,token).ConfigureAwait(false);break;
                default: throw new ArgumentException("Unknown diagnostic section.");
            }
            return s;
        }
        private static async Task DomainEvidence(DiagnosticSnapshot s,CancellationToken token)
        {
            string domain=null;bool? joined=null;
            try{using(var q=new ManagementObjectSearcher("SELECT Domain,PartOfDomain FROM Win32_ComputerSystem"))using(var rows=q.Get())foreach(ManagementObject row in rows)using(row){joined=Convert.ToBoolean(row["PartOfDomain"]);domain=Convert.ToString(row["Domain"]);}}catch(Exception e){s.Add("Computer identity",e.Message,"Unknown");}
            if(joined==true)await EvidenceCommand(s,"Authentication / secure channel","nltest.exe",new[]{"/sc_query:"+Toolkit.Core.Arguments.Host(domain)},token).ConfigureAwait(false);
            else s.Add("Authentication / secure channel",joined==false?"Not applicable — not AD joined":"Join state unknown",joined==false?"Not applicable":"Unknown");
            await EvidenceCommand(s,"Authentication / Kerberos ticket cache","klist.exe",new string[0],token).ConfigureAwait(false);
            s.Add("Authentication interpretation","A ticket cache is evidence of existing tickets, not a fresh authentication test. TCP LDAP reachability is not authenticated LDAP or TLS. Secure-channel query is separate; no trust reset is performed.");
            await EvidenceCommand(s,"Group Policy / applied user policy","gpresult.exe",new[]{"/scope","user","/r"},token).ConfigureAwait(false);
            s.Add("Group Policy / recent processing and warnings",PolicyRefresh.ReadEvents(DateTime.UtcNow.AddDays(-1),null),"Event evidence");
            s.Add("Device registration / MDM","Entra registration is reported separately. MDM enrollment/compliance is not established by join status; inspect your authorized management service.","Unknown");
        }
        private static async Task EvidenceCommand(DiagnosticSnapshot s,string label,string executable,string[] args,CancellationToken token)
        {
            var buffer=new Toolkit.Core.TerminalBuffer();
            try{var result=await new Toolkit.Core.ProcessEngine().RunAsync(Path.Combine(Environment.SystemDirectory,executable),args,Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),c=>buffer.Append(c.Text),token,true,10,true).ConfigureAwait(false);s.Add(label,"Native exit "+result.ExitCode+"\r\n"+buffer,result.ExitCode==0?"Query returned evidence":"Query failed / unavailable");}
            catch(OperationCanceledException){throw;}catch(Exception e){s.Add(label,buffer+"\r\n"+e.Message,"Unavailable");}
        }
        private static void BitLocker(DiagnosticSnapshot s,CancellationToken token)
        {
            try {
                using(var search=new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftVolumeEncryption","SELECT * FROM Win32_EncryptableVolume",new EnumerationOptions{Timeout=TimeSpan.FromSeconds(8),ReturnImmediately=false}))
                using(var rows=search.Get())foreach(ManagementObject row in rows)using(row){token.ThrowIfCancellationRequested();string drive=Convert.ToString(row["DriveLetter"]);s.Add(drive+" · ProtectionStatus",row["ProtectionStatus"]);
                    using(var conversion=row.InvokeMethod("GetConversionStatus",null,null)){s.Add(drive+" · ConversionStatus",conversion["ConversionStatus"]);s.Add(drive+" · EncryptionPercentage",conversion["EncryptionPercentage"]);s.Add(drive+" · Status method return",conversion["ReturnValue"]);}
                    using(var method=row.InvokeMethod("GetEncryptionMethod",null,null)){s.Add(drive+" · EncryptionMethod",method["EncryptionMethod"]);}
                }
            }catch(OperationCanceledException){throw;}catch(Exception e){s.Add("BitLocker provider",e.Message,"Unavailable / access denied");}
        }
        private static void UpdateHistory(DiagnosticSnapshot s)
        {
            object session = null, searcher = null, history = null;
            try
            {
                session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true)); searcher = Call(session, "CreateUpdateSearcher"); int count = Convert.ToInt32(Call(searcher, "GetTotalHistoryCount"));
                if (count == 0) { s.Add("Update history", "No records returned"); return; }
                history = Call(searcher, "QueryHistory", 0, Math.Min(count, 100));
                bool foundSuccess=false;int failed=0;
                for(int i=0;i<Convert.ToInt32(Get(history,"Count"));i++){object item=Get(history,"Item",i);try{int code=Convert.ToInt32(Get(item,"ResultCode"));string title=Convert.ToString(Get(item,"Title")),date=Convert.ToString(Get(item,"Date"));if(code==2&&Convert.ToInt32(Get(item,"Operation"))==1&&!foundSuccess){s.Add("Last successful update",date+" · "+title);foundSuccess=true;}if(code==4||code==5)failed++;s.Add("Update history item · "+date+" · "+title,code==2?"Succeeded":code==3?"Succeeded with errors":code==4?"Failed":code==5?"Aborted":"Incomplete / not started",code==4||code==5?"Warning":"");}finally{Release(item);}}
                if(!foundSuccess)s.Add("Last successful update","Not found in the latest 100 history entries","Unknown");if(failed>0)s.Add("Failed / aborted update history entries",failed+" in the latest 100 entries. Review Windows Update Settings.","Warning");
            }
            catch (Exception e) { s.Add("Update history", e.GetBaseException().Message, "Unavailable / access denied"); }
            finally { Release(history); Release(searcher); Release(session); }
        }
        internal static object Call(object o, string name, params object[] args) { return o.GetType().InvokeMember(name, System.Reflection.BindingFlags.InvokeMethod, null, o, args); }
        internal static object Get(object o, string name, params object[] args) { return o.GetType().InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, o, args); }
        internal static void Release(object o) { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); }
        public static DiagnosticSnapshot ScanUpdates()
        {
            var s = new DiagnosticSnapshot { Section = "Windows Update Diagnostics" }; object session = null, searcher = null, result = null, updates = null;
            try
            {
                session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true)); searcher = Call(session, "CreateUpdateSearcher"); result = Call(searcher, "Search", "IsInstalled=0 and IsHidden=0");
                int code = Convert.ToInt32(Get(result, "ResultCode")); updates = Get(result, "Updates"); int count = Convert.ToInt32(Get(updates, "Count"));
                s.Add("Scan result", code == 2 ? count == 0 ? "No updates found after completed scan" : "Updates available: " + count : "Scan failed or partial, result code " + code, code == 2 ? "Completed scan" : "Scan failed");
                for (int i = 0; i < Math.Min(count, 250); i++) { object item = Get(updates, "Item", i); try { s.Add("Available update " + (i+1), Get(item, "Title")); } finally { Release(item); } }
            }
            catch (Exception e) { s.Add("Scan", e.GetBaseException().Message, "Scan failed / access denied"); }
            finally { Release(updates); Release(result); Release(searcher); Release(session); } return s;
        }
        private static void UpdateEvents(DiagnosticSnapshot s)
        {
            try
            {
                var query = new System.Diagnostics.Eventing.Reader.EventLogQuery("Microsoft-Windows-WindowsUpdateClient/Operational", System.Diagnostics.Eventing.Reader.PathType.LogName, "*[System[(Level=2 or Level=3) and TimeCreated[timediff(@SystemTime)<=604800000]]]") { ReverseDirection = true };
                using (var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(query)) for (int i=0;i<100;i++) { using (var e = reader.ReadEvent(TimeSpan.FromSeconds(3))) { if(e==null) break; s.Add("Update event " + e.TimeCreated + " / " + e.Id, e.FormatDescription()); } }
            } catch(Exception e) { s.Add("Update events", e.Message, "Unavailable / access denied"); }
        }
        private static async Task Performance(DiagnosticSnapshot s, CancellationToken token)
        {
            var first = Sample(); var watch = Stopwatch.StartNew(); await Task.Delay(1000, token).ConfigureAwait(false); var second = SampleDetails(); double elapsed=watch.Elapsed.TotalMilliseconds;
            foreach(var item in second.OrderByDescending(x=>x.Memory)) { Tuple<double,long> prev; double? cpu = first.TryGetValue(item.Id, out prev) && prev.Item2==item.Start ? (double?)Math.Max(0,(item.Cpu-prev.Item1)/elapsed/Environment.ProcessorCount*100) : null; s.Add(item.Name+" PID "+item.Id, "RAM "+(item.Memory/1048576.0).ToString("N1")+" MB; CPU "+(cpu.HasValue?cpu.Value.ToString("N2")+"%":"Unavailable")+"; started "+new DateTime(item.Start).ToString("o")); }
            foreach(var item in second.Where(x=>first.ContainsKey(x.Id)&&first[x.Id].Item2==x.Start).OrderByDescending(x=>x.Cpu-first[x.Id].Item1).Take(15))s.Add("Top CPU · "+item.Name+" PID "+item.Id,Math.Max(0,(item.Cpu-first[item.Id].Item1)/elapsed/Environment.ProcessorCount*100).ToString("N2")+"% (one-second sample, whole-machine normalized)");
            Query(s,@"root\cimv2","SELECT * FROM Win32_Processor",new[]{"Name","LoadPercentage"},token);
            Query(s,@"root\cimv2","SELECT * FROM Win32_OperatingSystem",new[]{"Caption","TotalVisibleMemorySize","FreePhysicalMemory"},token);
            double totalRam,freeRam;var totalField=s.Records.LastOrDefault(x=>x.Name.EndsWith(" · TotalVisibleMemorySize"));var freeField=s.Records.LastOrDefault(x=>x.Name.EndsWith(" · FreePhysicalMemory"));
            if(totalField!=null&&freeField!=null&&Double.TryParse(totalField.Value,out totalRam)&&Double.TryParse(freeField.Value,out freeRam)&&totalRam>0){s.Add("Memory utilization",Math.Round((totalRam-freeRam)*100/totalRam,1)+"%");s.Add("Available RAM",(freeRam/1048576.0).ToString("N2")+" GiB");}
            Query(s,@"root\cimv2","SELECT * FROM Win32_StartupCommand",new[]{"Name","Command","Location","User"},token);
        }
        private sealed class Proc { public int Id; public string Name; public double Cpu; public long Memory,Start; }
        private static Dictionary<int,Tuple<double,long>> Sample() { return SampleDetails().ToDictionary(x=>x.Id,x=>Tuple.Create(x.Cpu,x.Start)); }
        private static List<Proc> SampleDetails() { var list=new List<Proc>(); foreach(var p in Process.GetProcesses()) using(p) try { list.Add(new Proc{Id=p.Id,Name=p.ProcessName,Cpu=p.TotalProcessorTime.TotalMilliseconds,Memory=p.WorkingSet64,Start=p.StartTime.Ticks}); } catch{} return list; }
    }
}
