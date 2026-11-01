using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using Toolkit.Core;
using Toolkit.Windows;
using Toolkit.Software;
using Toolkit.Reporting;

namespace Toolkit.App
{
    public sealed class ReportChoice:Bindable
    {
        public string Name{get;set;}
        private bool included=true;
        public bool Included{get{return included;}set{included=value;Changed("Included");}}
    }
    public sealed partial class MainViewModel
    {
        public ObservableCollection<DiagnosticRecord> DiagnosticRows{get;private set;}
        public ObservableCollection<InstalledApplication> InventoryRows{get;private set;}
        public ObservableCollection<ReportChoice> ReportSections{get;private set;}
        public string[] DiagnosticSections{get{return ExtendedDiagnostics.Sections;}}
        public string[] AdapterChoices{get;private set;}
        private string diagnosticSection;
        public string DiagnosticSection{get{return diagnosticSection;}set{if(diagnosticSection==value)return;diagnosticSection=value;currentDiagnostic=diagnosticSnapshots.ContainsKey(value)?diagnosticSnapshots[value]:null;SelectedDiagnostic=null;SelectedInventory=null;Changed("DiagnosticSection");RefreshDiagnosticView();if(DiagnosticRows!=null)FilterRows();if(CurrentPage=="Advanced Diagnostics")CollectDiagnostic(false);}}
        public string DiagnosticDescription{get{return "Read-only evidence for "+DiagnosticSection+". Missing evidence remains unknown. Select a row to inspect or copy complete values.";}}
        public string DiagnosticStamp{get{return currentDiagnostic==null?"Waiting for collection": "Collected: "+currentDiagnostic.Timestamp;}}
        public string DiagnosticStatus{get{return DiagnosticLoading?"Loading…":currentDiagnostic==null?"Not Available":"Ready";}}
        public bool IsNetworkDiagnostic{get{return DiagnosticSection=="Network Diagnostics+";}}
        public bool IsDeviceDiagnostic{get{return DiagnosticSection=="Devices & Drivers";}}
        public bool IsUpdateDiagnostic{get{return DiagnosticSection=="Windows Update Diagnostics";}}
        public bool IsDomainDiagnostic{get{return DiagnosticSection=="Domain Diagnostics";}}
        public bool IsSoftwareDiagnostic{get{return DiagnosticSection=="Installed Software";}}
        public bool IsGeneralDiagnostic{get{return !IsSoftwareDiagnostic;}}
        public ObservableCollection<DiagnosticRecord> DiagnosticHighlights{get;private set;}
        private DiagnosticRecord selectedDiagnostic;
        public DiagnosticRecord SelectedDiagnostic{get{return selectedDiagnostic;}set{selectedDiagnostic=value;Changed("SelectedDiagnostic");Changed("DiagnosticDetail");}}
        private InstalledApplication selectedInventory;
        public InstalledApplication SelectedInventory{get{return selectedInventory;}set{selectedInventory=value;Changed("SelectedInventory");Changed("DiagnosticDetail");Changed("CanUninstallSelected");Changed("UninstallSelectionInfo");System.Windows.Input.CommandManager.InvalidateRequerySuggested();}}
        public string DiagnosticDetail{get{return IsSoftwareDiagnostic?(SelectedInventory==null?"Select a software row.":SelectedInventory.Name+"\r\nVersion: "+SelectedInventory.Version+"\r\nPublisher: "+SelectedInventory.Publisher+"\r\nScope: "+SelectedInventory.Scope+"\r\nArchitecture: "+SelectedInventory.Architecture):(SelectedDiagnostic==null?"Select a result row to read its complete value.":SelectedDiagnostic.Name+"\r\n"+SelectedDiagnostic.Value+"\r\nState: "+SelectedDiagnostic.State+"\r\n"+SelectedDiagnostic.Timestamp);}}
        private bool outputExpanded;
        public bool OutputExpanded{get{return outputExpanded;}set{outputExpanded=value;Changed("OutputExpanded");}}
        private double outputHeight=160;
        public double OutputHeight{get{return outputHeight;}set{outputHeight=Math.Max(80,Math.Min(340,value));Changed("OutputHeight");}}
        private void RefreshDiagnosticView()
        {
            foreach(var name in new[]{"DiagnosticDescription","DiagnosticStamp","DiagnosticStatus","IsNetworkDiagnostic","IsDeviceDiagnostic","IsUpdateDiagnostic","IsDomainDiagnostic","IsSoftwareDiagnostic","IsGeneralDiagnostic","DiagnosticDetail","DiagnosticSummary","DiagnosticTechnicalText","DiagnosticSearchVisible","DiagnosticGroups","IsCardDiagnostic"})Changed(name);
            if(DiagnosticHighlights==null)return;DiagnosticHighlights.Clear();
            if(currentDiagnostic!=null)foreach(var row in currentDiagnostic.Records.Where(x=>new[]{"Warning","Critical","Failed","Action Required"}.Contains(x.State)).Take(4))DiagnosticHighlights.Add(row);
        }
        public string SelectedAdapter{get;set;}
        private string diagnosticSearch="";
        public string DiagnosticSearch{get{return diagnosticSearch;}set{diagnosticSearch=value;Changed("DiagnosticSearch");if(DiagnosticRows!=null)FilterRows();}}
        private bool problemDevicesOnly=true;public bool ProblemDevicesOnly{get{return problemDevicesOnly;}set{if(problemDevicesOnly==value)return;problemDevicesOnly=value;Changed("ProblemDevicesOnly");if(CurrentPage=="Advanced Diagnostics"&&IsDeviceDiagnostic)CollectDiagnostic(true);}}
        public bool RedactReports{get;set;}
        private string inventoryScope="all";
        public string InventoryScope{get{return inventoryScope;}set{inventoryScope=value;Changed("InventoryScope");if(InventoryRows!=null)FilterRows();}}
        public string SoftwareProvider{get;set;}
        public string ServiceToRestart{get;set;}
        public ICommand DiagnosticCommand{get;private set;}
        public ICommand ReportCommand{get;private set;}
        private readonly Dictionary<string,DiagnosticSnapshot> diagnosticSnapshots=new Dictionary<string,DiagnosticSnapshot>();
        private DiagnosticSnapshot previousNetwork,currentDiagnostic;
        private List<InstalledApplication> allInstalled=new List<InstalledApplication>();
        private void InitializeExtensions()
        {
            DiagnosticHighlights=new ObservableCollection<DiagnosticRecord>();DiagnosticRows=new ObservableCollection<DiagnosticRecord>();InventoryRows=new ObservableCollection<InstalledApplication>();ReportSections=new ObservableCollection<ReportChoice>(new[]{"System Information","Overview","BIOS & Hardware","Network Diagnostics+","Windows Update Diagnostics","Pending Reboot","Storage Health","Devices & Drivers","Installed Software","Performance","Domain Diagnostics","Services & Events","Maintenance Results","Speedtest"}.Select(n=>new ReportChoice{Name=n}));
            AdapterChoices=new[]{"All adapters"};SelectedAdapter=AdapterChoices[0];DiagnosticSection=DiagnosticSections[0];DiagnosticSearch="";ProblemDevicesOnly=true;RedactReports=true;InventoryScope="all";SoftwareProvider="Official direct";ServiceToRestart="Spooler";
            DiagnosticCommand=new Command(p=>RunDiagnostic(Convert.ToString(p)),()=>!Busy);
            ReportCommand=new Command(p=>SaveReport(Convert.ToString(p)),()=>!Busy);
        }
        private void Publish(DiagnosticSnapshot data)
        {
            if(data.Section=="Network Diagnostics+"&&diagnosticSnapshots.ContainsKey(data.Section))previousNetwork=diagnosticSnapshots[data.Section];
            diagnosticSnapshots[data.Section]=data;currentDiagnostic=data;RefreshDiagnosticView();FilterRows();
        }
        private void FilterRows()
        {
            string search=DiagnosticSearchVisible?DiagnosticSearch??"":"";DiagnosticRows.Clear();if(currentDiagnostic!=null)foreach(var row in currentDiagnostic.Records.Where(x=>(x.Name+" "+x.Value+" "+x.State).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0))DiagnosticRows.Add(row);
            InventoryRows.Clear();foreach(var row in allInstalled.Where(x=>(InventoryScope=="all"||x.Scope==InventoryScope)&&(x.Name+" "+x.Publisher+" "+x.Version).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0))InventoryRows.Add(row);
        }
        private void RunDiagnostic(string action)
        {
            if(action=="Collect"){CollectDiagnostic(true);return;}
            if(action=="CopyValue"||action=="CopyRow"){try{string value=action=="CopyValue"&&!IsSoftwareDiagnostic&&SelectedDiagnostic!=null?SelectedDiagnostic.Value:DiagnosticDetail;Clipboard.SetText(value??"");Status="Selected result copied.";}catch(Exception e){Status=e.Message;}return;}
            if(action=="ExportSection"){if(currentDiagnostic==null){Status="Collect this category first.";return;}SaveFile("HTML report|*.html","IT-Toolkit-diagnostic.html",DiagnosticReports.Html(DiagnosticReports.Prepare(new[]{currentDiagnostic},RedactReports,new[]{Environment.UserName,Environment.MachineName,Environment.UserDomainName})));return;}
            if(action=="Filter"){FilterRows();return;}
            if(action=="Adapters"){try{AdapterChoices=ExtendedDiagnostics.Adapters();Changed("AdapterChoices");SelectedAdapter=AdapterChoices[0];Changed("SelectedAdapter");Status="Adapter choices refreshed.";}catch(Exception e){Status=e.Message;}return;}
            if(action=="Compare"){string comparison=DiagnosticSnapshot.Compare(previousNetwork,diagnosticSnapshots.ContainsKey("Network Diagnostics+")?diagnosticSnapshots["Network Diagnostics+"]:null);Start("Network comparison",token=>Task.FromResult(comparison));return;}
            if(action=="LoadSnapshot") {try{var d=new OpenFileDialog{Filter="Snapshot JSON|*.json"};if(d.ShowDialog()==true){if(new FileInfo(d.FileName).Length>8*1024*1024)throw new IOException("Snapshot exceeds 8 MiB.");var s=new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Deserialize<DiagnosticSnapshot>(File.ReadAllText(d.FileName));if(s==null||s.Section!="Network Diagnostics+"||s.Records==null)throw new IOException("Select a saved network snapshot.");previousNetwork=s;Status="Comparison baseline loaded. Click Compare network snapshots.";}}catch(Exception e){Status=e.Message;}return;}
            if(action=="SaveSnapshot"){if(currentDiagnostic==null){Status="Collect diagnostics first.";return;}SaveFile("Snapshot JSON|*.json","IT-Toolkit-snapshot.json",new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Serialize(currentDiagnostic));return;}
            if(action=="Speedtest"){if(Confirm(new OoklaWebsiteProvider().Availability+"\r\nOpen speedtest.net in your browser? No test runs automatically; bandwidth is used only when you start it there."))OpenWeb("https://www.speedtest.net/");return;}
            if(action=="DeviceManager"){OpenTool("devmgmt.msc");return;}
            if(action=="Troubleshoot"){OpenTool("ms-settings:troubleshoot");return;}
            if(action=="Support"){Start("Manufacturer support",token=>{var s=new DiagnosticSnapshot{Section="Devices & Drivers"};ExtendedDiagnostics.Query(s,@"root\cimv2","SELECT * FROM Win32_ComputerSystem",new[]{"Manufacturer","Model"},token);string text=s.ToString();Dispatch(()=>{if(text.IndexOf("Lenovo",StringComparison.OrdinalIgnoreCase)>=0)OpenWeb("https://pcsupport.lenovo.com/");else if(text.IndexOf("Dell",StringComparison.OrdinalIgnoreCase)>=0)OpenWeb("https://www.dell.com/support/home/");else Status="No Lenovo/Dell match; use your manufacturer's official support site.";});return Task.FromResult(text);});return;}
            if(action=="Renew"||action=="Release"||action=="RestartService"||action=="StartService"||action=="StopService")
            {
                string operation;
                if(action.EndsWith("Service"))operation=action+":"+ServiceToRestart;
                else{Guid id;if(!Guid.TryParse((SelectedAdapter??"").Split('|')[0].Trim(),out id)){Status="Select one adapter, not All adapters.";return;}operation=(action=="Release"?"DhcpRelease:":"DhcpRenew:")+id.ToString();}
                if(!Operations.IsElevated(operation)){Status="Unsupported repair action.";return;}
                if(!Confirm("Run "+action+"? This can interrupt network connectivity, remote sessions, printing or update transfers. DHCP release removes the selected lease. Native UAC is requested only if needed; no enterprise settings will be reset."))return;
                Start(operation,async token=>{
                    OutputExpanded=true;
                    if(action=="StartService"||action=="StopService"){
                        string state=await Task.Run(()=>QuickRepair.ReadServiceState(operation.Split(':')[1])).ConfigureAwait(false);
                        string desired=action=="StartService"?"Running":"Stopped";
                        StateWorkflow.Emit(Queue,"Check: service state "+state);
                        if(state==desired)return RecordResult(StateWorkflow.Result(ChangeVerdict.AlreadyConfigured,"Already "+desired+" — No Changes Required; no elevation."));
                    }
                    var result=await privilege.ExecuteAsync(operation,Queue,token).ConfigureAwait(false);if(result.ExitCode!=0&&result.ExitCode!=3010)throw new InvalidOperationException("Repair failed, exit "+result.ExitCode);return RecordResult(result)??"Verification Inconclusive — no worker evidence returned.";});return;
            }
            if(action=="InventoryCsv"||action=="InventoryJson"){SaveFile(action=="InventoryCsv"?"CSV|*.csv":"JSON|*.json",action=="InventoryCsv"?"Installed-software.csv":"Installed-software.json",action=="InventoryCsv"?InstalledInventory.Csv(InventoryRows):new JavaScriptSerializer().Serialize(InventoryRows));return;}
            string section=DiagnosticSection,adapter=SelectedAdapter;bool problems=ProblemDevicesOnly;
            if(action=="Scan"&&!Confirm("Perform a read-only Windows Update availability scan using the configured update service? This contacts your configured update source and can take several minutes. No updates will be installed."))return;
            Start(action=="Scan"?"Windows Update scan":section,async token=>
            {
                if(section=="Installed Software"&&action=="Collect")
                {var inventory=await BoundedWork.Run("Software inventory",()=>Task.FromResult(InstalledInventory.Read()),30,token).ConfigureAwait(false);var s=new DiagnosticSnapshot{Section=section};foreach(var row in inventory.Items)s.Add(row.Name,row.Version+" · "+row.Publisher+" · "+row.Scope+" · "+row.InstallDate);foreach(var error in inventory.Errors)s.Add("Inventory error",error,"Unknown");Dispatch(()=>{allInstalled=inventory.Items;Publish(s);});return "Inventory refreshed: "+inventory.Items.Count+" items; "+(inventory.Complete?"registry locations read":"partial / access restricted")+". Store/MSIX apps without uninstall records may be absent.";}
                Dispatch(()=>OutputExpanded=false);
                DiagnosticSnapshot result;
                if(action=="Scan")result=await BoundedWork.Run("Windows Update scan",()=>Task.FromResult(ExtendedDiagnostics.ScanUpdates()),180,token).ConfigureAwait(false);
                else if(action=="Internet")result=await Internet(token).ConfigureAwait(false);
                else if(action=="SecureChannel")return await BoundedWork.Run("Secure channel",()=>SecureChannel(token),45,token).ConfigureAwait(false);
                else result=await BoundedWork.Run(section,()=>new ExtendedDiagnostics().CollectAsync(section,adapter,problems,token),45,token).ConfigureAwait(false);
                Dispatch(()=>Publish(result));return result.ToString();
            });
        }
        private async Task<DiagnosticSnapshot> Internet(CancellationToken token)
        {
            var s=new DiagnosticSnapshot{Section="Network Diagnostics+"};string target=Arguments.Host(NetworkTarget);var net=new NetworkDiagnostics();
            foreach(var kind in new[]{"Dns","Tcp","Ping"}){try{var clock=System.Diagnostics.Stopwatch.StartNew();var r=await net.RunAsync(kind,target,443,3000,token).ConfigureAwait(false);s.Add(kind,r.Text+" · "+clock.ElapsedMilliseconds+" ms",r.Status);}catch(OperationCanceledException){throw;}catch(Exception e){s.Add(kind,e.Message,"Failure");}}
            try{using(var handler=new HttpClientHandler{AllowAutoRedirect=false})using(var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(10)})using(var request=new HttpRequestMessage(HttpMethod.Head,new UriBuilder("https",target).Uri))using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))s.Add("HTTPS","HTTP status "+(int)response.StatusCode+"; TLS handshake succeeded. Redirects not followed; this does not establish full internet access.",response.IsSuccessStatusCode?"Reached":"HTTP response");}catch(OperationCanceledException){token.ThrowIfCancellationRequested();s.Add("HTTPS","Timed out","Failure");}catch(Exception e){s.Add("HTTPS",e.Message,"TLS/HTTP failure");}return s;
        }
        private async Task<string> SecureChannel(CancellationToken token)
        {
            string domain=null;bool joined=false;
            using(var search=new System.Management.ManagementObjectSearcher("SELECT Domain,PartOfDomain FROM Win32_ComputerSystem"))using(var rows=search.Get())foreach(System.Management.ManagementObject row in rows)using(row){joined=Convert.ToBoolean(row["PartOfDomain"]);domain=Convert.ToString(row["Domain"]);}
            if(!joined)return "Not applicable: computer is not AD domain joined.";
            var r=await new ProcessEngine().RunAsync(Path.Combine(Environment.SystemDirectory,"nltest.exe"),new[]{"/sc_query:"+Arguments.Host(domain)},Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),Queue,token,true,30).ConfigureAwait(false);
            return "Read-only secure channel query exit "+r.ExitCode+". See native status. Missing nltest/access denied is unavailable evidence, not a broken trust. No reset was requested.";
        }
        private void OpenTool(string name){try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(name.StartsWith("ms-settings:")?name:Path.Combine(Environment.SystemDirectory,name)){UseShellExecute=true});}catch(Exception e){Status=e.Message;}}
        private void SaveFile(string filter,string name,string text){try{var d=new SaveFileDialog{Filter=filter,FileName=name};if(d.ShowDialog()==true){File.WriteAllText(d.FileName,text,new UTF8Encoding(false));Status="Exported. Review content before sharing.";}}catch(Exception e){Status=e.Message;}}
        private void SaveReport(string format)
        {
            var chosen=new HashSet<string>(ReportSections.Where(x=>x.Included).Select(x=>x.Name));var sections=new List<DiagnosticSnapshot>();var identities=new List<string>{Environment.UserName,Environment.MachineName,Environment.UserDomainName,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)};
            if(snapshot!=null){foreach(var sec in snapshot.Sections)foreach(var field in sec.Fields)if(System.Text.RegularExpressions.Regex.IsMatch(field.Name,"(?i)serial|user|domain|computer"))identities.Add(field.Value);if(chosen.Contains("System Information")){var s=new DiagnosticSnapshot{Section="System Information",Timestamp=snapshot.CollectedAt.ToString("o")};foreach(var sec in snapshot.Sections)foreach(var field in sec.Fields)s.Add(sec.Name+" · "+field.Name,field.Value);sections.Add(s);}}
            foreach(var s in diagnosticSnapshots.Values)if(chosen.Contains(s.Section))sections.Add(s);
            if(chosen.Contains("Maintenance Results")){var s=new DiagnosticSnapshot{Section="Maintenance Results"};foreach(var text in completed)s.Add("Operation",text);sections.Add(s);}
            if(chosen.Contains("Speedtest")){var s=new DiagnosticSnapshot{Section="Speedtest"};s.Add("Provider",new OoklaWebsiteProvider().Availability,"Browser only / no captured results");sections.Add(s);}
            foreach(string name in chosen)if(!sections.Any(s=>s.Section==name)){var s=new DiagnosticSnapshot{Section=name};s.Add("Collection","Not collected in this session","Not performed");sections.Add(s);}
            var prepared=DiagnosticReports.Prepare(sections,RedactReports,identities);SaveFile(format=="JSON"?"JSON report|*.json":"HTML report|*.html","IT-Toolkit-report."+(format=="JSON"?"json":"html"),format=="JSON"?DiagnosticReports.Json(prepared):DiagnosticReports.Html(prepared));
        }
    }
}
