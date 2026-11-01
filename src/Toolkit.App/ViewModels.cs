using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Toolkit.Core;
using Toolkit.Privilege;
using Toolkit.Reporting;
using Toolkit.Software;
using Toolkit.Windows;

namespace Toolkit.App
{
    public sealed class NavigationSelection : System.Windows.Data.IMultiValueConverter
    {
        public object Convert(object[] values, Type type, object parameter, CultureInfo culture)
        { return values.Length == 2 && values[0] is string && values[1] is string && String.Equals((string)values[0], (string)values[1], StringComparison.Ordinal); }
        public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
    public class Bindable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Changed(string name) { var handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); }
    }
    public sealed class Command : ICommand
    {
        private readonly Action<object> run; private readonly Func<bool> enabled;
        public Command(Action<object> action, Func<bool> canRun = null) { run = action; enabled = canRun ?? (() => true); }
        public bool CanExecute(object value) { return enabled(); }
        public void Execute(object value) { run(value); }
        public event EventHandler CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
    }
    public sealed class DashboardCard
    {
        public string Name { get; set; }
        public InformationField[] Fields { get; set; }
        public double Usage { get; set; }
        public bool HasUsage { get; set; }
    }
    public sealed class PackageRow : Bindable
    {
        public PackageDefinition Definition { get; private set; }
        public string Name { get { return Definition.Name; } }
        public string Id { get { return Definition.Id; } }
        private bool selected;
        public bool Selected { get { return selected; } set { selected = value; Changed("Selected"); } }
        private string state = "Not checked";
        public string State { get { return state; } set { state = value; Changed("State"); } }
        public string Provider { get { return "WinGet / official vendor page"; } }
        private string availableVersion = "Unknown — use Package details";
        public string AvailableVersion { get { return availableVersion; } set { availableVersion=value; Changed("AvailableVersion"); } }
        public PackageRow(PackageDefinition definition) { Definition = definition; }
    }
    public sealed partial class MainViewModel : Bindable, IDisposable
    {
        private readonly ILogger log;
        private readonly IInventoryService inventory;
        private readonly PrivilegeService privilege;
        private readonly ConcurrentQueue<OutputChunk> chunks = new ConcurrentQueue<OutputChunk>();
        private readonly TerminalBuffer terminal = new TerminalBuffer();
        private readonly StringBuilder transcript = new StringBuilder();
        private readonly List<string> completed = new List<string>();
        private readonly DispatcherTimer timer;
        private CancellationTokenSource cancellation;
        private DateTimeOffset began;
        private InventorySnapshot snapshot;
        private bool refreshing, busy, closed, transcriptTruncated;
        private string currentPage = "Dashboard", title = "Dashboard", status = "Ready", output = "", operation = "No operation", exitCode = "—", elapsed = "0s", outcome = "Idle", stamp = "Not collected", warnings = "";
        private double progress;
        private bool indeterminate;
        public string Version { get { return Release.Version; } }
        public string CurrentPage { get { return currentPage; } private set { currentPage = value; Changed("CurrentPage"); } }
        public string PageTitle { get { return title; } private set { title = value; Changed("PageTitle"); } }
        public string Status { get { return status; } private set { status = value; Changed("Status"); } }
        public bool Busy { get { return busy; } private set { busy = value; Changed("Busy"); Changed("Idle"); Changed("CanUninstallSelected");Changed("CanStartService");Changed("CanStopService");Changed("CanRestartService");CommandManager.InvalidateRequerySuggested(); } }
        public bool Idle { get { return !busy; } }
        public bool Refreshing { get { return refreshing; } private set { refreshing = value; Changed("Refreshing"); CommandManager.InvalidateRequerySuggested(); } }
        public string Output { get { return output; } private set { output = value; Changed("Output"); } }
        public string Operation { get { return operation; } private set { operation = value; Changed("Operation"); } }
        public string ExitCode { get { return exitCode; } private set { exitCode = value; Changed("ExitCode"); } }
        public string Elapsed { get { return elapsed; } private set { elapsed = value; Changed("Elapsed"); } }
        public string Outcome { get { return outcome; } private set { outcome = value; Changed("Outcome"); } }
        public double Progress { get { return progress; } private set { progress = value; Changed("Progress"); } }
        public bool Indeterminate { get { return indeterminate; } private set { indeterminate = value; Changed("Indeterminate"); } }
        public string SnapshotStamp { get { return stamp; } private set { stamp = value; Changed("SnapshotStamp"); } }
        public string Warnings { get { return warnings; } private set { warnings = value; Changed("Warnings"); } }
        public string LogPath { get { return log.Path; } }
        public string LogStatus { get { return log.Status; } }
        public string Identity { get; private set; }
        public bool AutoScroll { get; set; }
        public string NetworkTarget { get; set; }
        public string TcpPort { get; set; }
        public string Timeout { get; set; }
        private string serviceSearch="";public string ServiceSearch{get{return serviceSearch;}set{serviceSearch=value;Changed("ServiceSearch");FilterServices();}}
        private string serviceStatus="All";public string ServiceStatus{get{return serviceStatus;}set{serviceStatus=value;Changed("ServiceStatus");FilterServices();}}
        public string EventLog { get; set; }
        public string EventSeverity { get; set; }
        public string EventDays { get; set; }
        public string EventMaximum { get; set; }
        public string RemoteTarget { get; set; }
        public bool EnterpriseEnabled { get; set; }
        public string InstallScope { get; set; }
        public PackageRow SelectedPackage { get; set; }
        public ObservableCollection<InformationSection> Sections { get; private set; }
        public ObservableCollection<DashboardCard> Cards { get; private set; }
        public ObservableCollection<PackageRow> Packages { get; private set; }
        public ICommand NavigateCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand CopyInventoryCommand { get; private set; }
        public ICommand CopySectionCommand { get; private set; }
        public ICommand RunNetworkCommand { get; private set; }
        public ICommand ServicesCommand { get; private set; }
        public ICommand EventsCommand { get; private set; }
        public ICommand MaintenanceCommand { get; private set; }
        public ICommand RemoteCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand CopyOutputCommand { get; private set; }
        public ICommand ExportTextCommand { get; private set; }
        public ICommand ExportHtmlCommand { get; private set; }
        public ICommand SettingsCommand { get; private set; }
        public ICommand SoftwareCommand { get; private set; }
        public ICommand ProbeCommand { get; private set; }

        private readonly bool automaticCollection;
        private readonly Func<string,CancellationToken,Task<DiagnosticSnapshot>> diagnosticProvider;
        public MainViewModel(ILogger logger,bool automaticCollection=true,Func<string,CancellationToken,Task<DiagnosticSnapshot>> diagnosticProvider=null)
        {
            this.automaticCollection=automaticCollection;this.diagnosticProvider=diagnosticProvider;log = logger; inventory = new WindowsInventoryService(); privilege = new PrivilegeService(log);
            using (var id = System.Security.Principal.WindowsIdentity.GetCurrent()) Identity = Environment.MachineName + " | " + id.Name + " | " + (new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator) ? "Elevated GUI (started externally)" : "Standard user");
            InitializeBrowsers();
            InitializeUninstall();
            InitializeShortcut();
            Sections = new ObservableCollection<InformationSection>(); Cards = new ObservableCollection<DashboardCard>(); Packages = new ObservableCollection<PackageRow>(Catalog.All().Select(x => new PackageRow(x))); SelectedPackage = Packages[0];
            AutoScroll = true; NetworkTarget = ""; TcpPort = "443"; Timeout = "3000"; ServiceSearch = ""; ServiceStatus = "All"; EventLog = "System"; EventSeverity = "Both"; EventDays = "7"; EventMaximum = "100"; RemoteTarget = ""; InstallScope = "default";
            NavigateCommand = new Command(p => { string page = Convert.ToString(p); if (!new[] { "Advanced Diagnostics", "Dashboard", "System Information", "Network Diagnostics", "Services & Events", "Windows Maintenance", "Windows Update", "Software", "Reports", "Settings / About" }.Contains(page)) return; CurrentPage = page; PageTitle = page; if(page=="Advanced Diagnostics")CollectDiagnostic(false);if(automaticCollection&&page=="Services & Events"&&ServiceRows.Count==0)LoadServices(); if (page == "Dashboard" || page == "System Information") Refresh(false); });
            RefreshCommand = new Command(p => Refresh(true), () => !Refreshing && !Busy);
            CopyInventoryCommand = new Command(p => Copy(snapshot == null ? "No inventory collected." : snapshot.ToString()));
            CopySectionCommand = new Command(p => { var section = p as InformationSection; if (section != null) Copy(section.ToString()); });
            RunNetworkCommand = new Command(p => Start("Network · " + p, async token => { int port = Integer(TcpPort, 1, 65535, "TCP port"), ms = Integer(Timeout, 250, 30000, "Timeout"); var result = await new NetworkDiagnostics().RunAsync(Convert.ToString(p), NetworkTarget, port, ms, token); return result.Status + "\r\n" + result.Text; }), () => !Busy);
            ServicesCommand = new Command(p => LoadServices(),()=>!Busy&&!serviceLoading);
            EventsCommand = new Command(p => LoadEvents(false),()=>!eventLoading);
            MaintenanceCommand = new Command(p => RunMaintenance(Convert.ToString(p)), () => !Busy);
            ProbeCommand = new Command(p => { if (Confirm("Run the read-only administrator helper probe? Windows will ask for elevation. No repair or installation runs.")) Start("WorkerProbe", token => privilege.RunAsync(token)); }, () => !Busy);
            RemoteCommand = new Command(p => RemoteUpdate(), () => !Busy);
            CancelCommand = new Command(p => { if (cancellation != null) { cancellation.Cancel(); Outcome = "Cancelling"; Status = "Cancellation requested. Active servicing/installers finish safely; subsequent work is skipped."; } }, () => Busy);
            CopyOutputCommand = new Command(p => Copy(Transcript()));
            ExportTextCommand = new Command(p => ExportText()); ExportHtmlCommand = new Command(p => ExportHtml());
            SettingsCommand = new Command(p => OpenSettings(Convert.ToString(p)));
            SoftwareCommand = new Command(p => RunSoftware(Convert.ToString(p)), () => !Busy);
            InitializeExtensions();
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) }; timer.Tick += Tick; timer.Start();
        }
        public void Initialize() { Refresh(false); }
        private static int Integer(string text, int min, int max, string label)
        { int result; if (!Int32.TryParse(text, out result) || result < min || result > max) throw new ArgumentException(label + " must be " + min + "–" + max + "."); return result; }
        private static bool Confirm(string text) { return MessageBox.Show(text, Release.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes; }
        private static void Dispatch(Action action) { Application.Current.Dispatcher.BeginInvoke(action); }
        private void Copy(string text)
        { try { Clipboard.SetText(text ?? ""); Status = "Copied. Review device/account/network information before sharing."; } catch { Status = "Clipboard unavailable. Select text and press Ctrl+C."; } }
        private async void Refresh(bool force)
        {
            if (Refreshing || closed) return;
            if (!force && snapshot != null && DateTimeOffset.Now - snapshot.CollectedAt < TimeSpan.FromMinutes(5)) return;
            Refreshing = true; SnapshotStamp = "Collecting in background; previous snapshot remains visible."; var watch = Stopwatch.StartNew(); log.Write("InventoryStart", "Inventory", "Running");
            try
            {
                var next = await inventory.CollectAsync(CancellationToken.None);
                if (closed) return;
                snapshot = next; Sections.Clear(); foreach (var section in next.Sections) Sections.Add(section);
                Warnings = next.Warnings; SnapshotStamp = "Collected " + next.CollectedAt.ToLocalTime().ToString("HH:mm:ss") + " · cached for 5 minutes · usage values are snapshots";
                BuildCards(next); log.Write("InventoryComplete", "Inventory", next.Warnings.Length == 0 ? "Collected" : "Partial", duration: watch.Elapsed.TotalMilliseconds);
            }
            catch (Exception e) { SnapshotStamp = "Collection failed. Refresh to retry. " + e.GetType().Name; log.Write("InventoryFailed", "Inventory", "Failed", e.Message); }
            finally { if (!closed) Refreshing = false; }
        }
        private void BuildCards(InventorySnapshot data)
        {
            Cards.Clear();
            Cards.Add(Card(data, "Workstation", "Identity", "Computer", "Current user", "Interactive user", "Administrator token", "Domain / workgroup"));
            Cards.Add(Card(data, "Windows", "Windows", "Edition", "Display version", "Build", "Uptime"));
            Cards.Add(Card(data, "Hardware", "Hardware", "Manufacturer", "Model", "Processor", "Installed RAM"));
            Cards.Add(Card(data, "Memory", "Memory", "Used RAM", "Usable RAM", "Available RAM", "Used percent"));
            Cards.Add(Card(data, "System disk", "Storage", "System drive", "System capacity", "System available", "System used percent"));
            Cards.Add(Card(data, "Network", "Network", "Connected IPv4", "Default gateway", "Connectivity"));
        }
        private static DashboardCard Card(InventorySnapshot data, string title, string section, params string[] names)
        {
            double used; string value = data.Find(section, section == "Memory" ? "Used percent" : "System used percent");
            bool bar = (section == "Memory" || section == "Storage") && Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out used);
            Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out used);
            return new DashboardCard { Name = title, Fields = names.Select(n => new InformationField(n, data.Find(section, n))).ToArray(), Usage = used, HasUsage = bar };
        }
        private void Queue(OutputChunk chunk)
        {
            log.Write("CommandOutput", Operation, "Running", chunk.Text);
            // Bounded queue: never stall the process; report loss rather than fabricate a complete transcript.
            if ((chunk.Text ?? "").Length > 4096)
            {
                for (int start = 0; start < chunk.Text.Length; start += 4096)
                { if (chunks.Count >= 2048) { Interlocked.Exchange(ref dropped, 1); break; } chunks.Enqueue(new OutputChunk { Channel = chunk.Channel, Text = chunk.Text.Substring(start, Math.Min(4096, chunk.Text.Length - start)), MeasuredPercent = chunk.MeasuredPercent }); }
            }
            else if (chunks.Count < 2048) chunks.Enqueue(chunk);
            else Interlocked.Exchange(ref dropped, 1);
        }
        private int dropped;
        private void Tick(object sender, EventArgs args)
        {
            if (Busy) Elapsed = ((int)(DateTimeOffset.Now - began).TotalSeconds) + "s";
            OutputChunk chunk; int budget = 32; bool changed = false;
            if (Interlocked.Exchange(ref dropped, 0) != 0) { chunks.Enqueue(new OutputChunk { Channel = "status", Text = "\r\n[UI queue overflow: some live output was dropped; worker/operation log may contain more.]\r\n" }); }
            while (budget-- > 0 && chunks.TryDequeue(out chunk))
            {
                string text = (chunk.Channel == "stderr" ? "\r\n[stderr] " : "") + chunk.Text;
                terminal.Append(text); changed = true;
                if (transcript.Length < 2 * 1024 * 1024) transcript.Append(text); else transcriptTruncated = true;
                if ((chunk.Text ?? "").Contains("Progress is indeterminate")) Indeterminate = true;
                if (chunk.MeasuredPercent.HasValue) { Progress = Math.Max(0, Math.Min(100, chunk.MeasuredPercent.Value)); Indeterminate = false; }
            }
            if (changed) Output = terminal.ToString();
            Changed("LogStatus");
        }
        private string Transcript() { return transcript.ToString() + (transcriptTruncated ? "\r\n[Retained transcript limit reached: 2 Mi characters.]" : ""); }
        private string operationVerdict;
        private string RecordResult(ProcessResult result){operationVerdict=result.Verdict;return result.Summary;}
        private async void Start(string name, Func<CancellationToken, Task<string>> action)
        {
            if (Busy || closed) return;
            while (chunks.TryDequeue(out pendingDiscard)) { }
            operationVerdict=null;transcript.Clear(); transcriptTruncated = false; Output = ""; terminal.Reset(); ExitCode = "—"; Elapsed = "0s"; Operation = name; Outcome = "Starting"; began = DateTimeOffset.Now; Busy = true; Progress = 0; Indeterminate = true; Status = "Running · " + name;
            cancellation = new CancellationTokenSource(); var token = cancellation.Token; string correlation = Guid.NewGuid().ToString("N"); log.Write("OperationStart", name, "Running", correlation: correlation);
            try
            {
                Outcome = "Running";
                string result = await BoundedWork.Run("Operation " + name, () => action(token), 28800, CancellationToken.None);
                if (closed) return;
                Queue(new OutputChunk { Channel = "status", Text = "\r\n" + result + "\r\n" });
                Outcome = token.IsCancellationRequested ? "Completed — cancellation was advisory; review final result" : operationVerdict??"Completed — review evidence";
                Status = Outcome + " · " + name;
                if(name == "Services" || name == "Event logs") { var observation = new DiagnosticSnapshot { Section="Services & Events" }; observation.Add(name,result); diagnosticSnapshots[observation.Section]=observation; }
                string reportResult = name == "Event logs" ? "Event log query completed. Event message bodies are excluded from default reports; use explicit text export to include them." : result;
                completed.Add(name + "\r\nStarted: " + began.ToString("o") + "\r\nCompleted: " + DateTimeOffset.Now.ToString("o") + "\r\n" + reportResult);
                if (completed.Count > 100) completed.RemoveAt(0);
                log.Write("OperationComplete", name, operationVerdict??"Completed", "Operation reached final state; details are in CommandOutput entries.", (DateTimeOffset.Now - began).TotalMilliseconds, correlation);
            }
            catch (OperationCanceledException e) { Outcome = "Cancelled"; Status = "Cancelled · " + name; Queue(new OutputChunk { Channel = "status", Text = e.Message + "\r\n" }); log.Write("OperationCancelled", name, "Cancelled", duration: (DateTimeOffset.Now - began).TotalMilliseconds, correlation: correlation); }
            catch (TimeoutException e) { Outcome = "Timed Out"; Status = e.Message; Queue(new OutputChunk { Channel="stderr", Text=e.Message+"\r\n" }); }
            catch (Exception e)
            {
                string reason = FriendlyError(e); Outcome = "Failed"; Status = "Failed · " + name; Queue(new OutputChunk { Channel = "stderr", Text = reason + "\r\nLog: " + log.Path + "\r\n" }); log.Write("OperationFailed", name, "Failed", e.GetType().Name + ": " + e.Message, (DateTimeOffset.Now - began).TotalMilliseconds, correlation);
                completed.Add(name + "\r\nFailed: " + reason);
            }
            finally { if(completed.Count>100)completed.RemoveRange(0,completed.Count-100); if (!closed) { Tick(this, EventArgs.Empty); Busy = false; Indeterminate = false; } cancellation.Dispose(); cancellation = null; }
        }
        private OutputChunk pendingDiscard;
        private static string FriendlyError(Exception error)
        {
            var win32 = error as System.ComponentModel.Win32Exception;
            if (win32 != null) return error.Message + " (Windows error " + win32.NativeErrorCode + "). Check permissions/application control; no policy was changed.";
            return error.Message + " (" + error.GetType().Name + ").";
        }
        private void RunMaintenance(string name)
        {
            if (Busy) return;
            if(name=="GpUpdate"){
                try{using(var process=Process.Start(UserActions.GroupPolicy(Environment.SystemDirectory))){}Status="Group Policy command window launched. Respond to Windows prompts manually; policy completion is not verified.";}
                catch(Exception e){Status="Could not launch Group Policy command window: "+e.Message;}return;
            }
            try { if(name!="GpUpdate")Operations.Command(name); } catch { return; }
            if (name != "ResultUser" && name != "ResultComputer" && !Confirm("Run " + name + "? This may apply policy or repair Windows. No automatic restart/logoff. Active servicing is not forcibly cancelled. Review the final exit code and original output.")) return;
            OutputExpanded=true;
            Start(name, async token =>
            {
                if(name=="NetFx3"){
                    string state=await Task.Run(()=>MaintenanceWorkflow.ReadFeatureWithoutElevation()).ConfigureAwait(false);
                    Queue(new OutputChunk{Channel="status",Text="Check: .NET Framework 3.5 = "+state+"\r\n"});
                    if(state=="Enabled"){operationVerdict=ChangeVerdict.AlreadyConfigured.ToString();return ".NET Framework 3.5: Already Enabled — No Action Required. No elevation or DISM enablement performed.";}
                }
                ProcessResult result;
                if (Operations.IsElevated(name)) result = await privilege.ExecuteAsync(name, Queue, token).ConfigureAwait(false);
                else
                {
                    var command = Operations.Command(name);
                    result = await new ProcessEngine().RunAsync(Path.Combine(Environment.SystemDirectory, command[0]), command.Skip(1), Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage), Queue, token, true, 60).ConfigureAwait(false);
                }
                Dispatch(() => ExitCode = name=="GpUpdate"?"N/A — native API; see per-scope error codes":result.NativeExitCode.HasValue?result.NativeExitCode.Value.ToString():result.ProcessId>0?result.ExitCode.ToString():"Not executed");
                if(result.Summary!=null)return RecordResult(result);
                if(result.ExitCode != 0 && !result.RebootRequired) throw new InvalidOperationException("Command failed, exit " + result.ExitCode + ". Review native output.");
                return name+": diagnostic command completed, native exit "+result.ExitCode+". Review original policy evidence above; no configuration change was requested.";
            });
        }
        private void RemoteUpdate()
        {
            if (!EnterpriseEnabled) { Status = "Enable authorized enterprise operations before remote GPUpdate."; return; }
            string target;
            try { target = Arguments.Host(RemoteTarget); } catch (Exception e) { Status = e.Message; return; }
            if (!Confirm("Schedule Group Policy refresh on " + target + " using your current Windows identity? Requires authorized domain permissions, GroupPolicy RSAT and preconfigured remote management/firewall. No credentials or policy changes are performed. Submission is not proof of completion.")) return;
            Start("Remote GPUpdate", async token =>
            {
                string name = target; IPAddress ip;
                if (IPAddress.TryParse(name, out ip))
                {
                    var lookup = Dns.GetHostEntryAsync(ip);
                    if (await Task.WhenAny(lookup, Task.Delay(5000, token)).ConfigureAwait(false) != lookup) throw new TimeoutException("IP reverse lookup timed out. Use the authorized computer FQDN.");
                    name = Arguments.Host((await lookup.ConfigureAwait(false)).HostName);
                    var forward = await Dns.GetHostAddressesAsync(name).ConfigureAwait(false);
                    if (!forward.Any(x => x.Equals(ip))) throw new InvalidOperationException("Reverse/forward DNS did not confirm the IP. Use the computer's authorized hostname; an IP alone does not establish authentication.");
                }
                DateTime submitted=DateTime.UtcNow;
                Queue(new OutputChunk{Channel="status",Text="Check: reading authorized remote Group Policy evidence; explicit force refresh remains requested.\r\n"});
                StateWorkflow.Emit(Queue,await PolicyRefresh.RemoteEvidence(name,submitted.AddMinutes(-10),token).ConfigureAwait(false));
                string ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
                // Host validation excludes apostrophes and all command syntax. Fixed adapter, no general console.
                string script = "& { param([string]$Computer) $ErrorActionPreference='Stop'; Import-Module (Join-Path $PSHOME 'Modules\\GroupPolicy\\GroupPolicy.psd1') -ErrorAction Stop; Invoke-GPUpdate -Computer $Computer -Force -RandomDelayInMinutes 0 -ErrorAction Stop; Write-Output 'Remote refresh request submitted; completion has not been verified.' } '" + name + "'";
                var r = await new ProcessEngine().RunAsync(ps, new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script }, Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage), Queue, token, true, 90, restrictedEnvironment: true).ConfigureAwait(false);
                StateWorkflow.Emit(Queue,"Post-check: "+await PolicyRefresh.RemoteEvidence(name,submitted,CancellationToken.None).ConfigureAwait(false));
                operationVerdict=r.ExitCode==0?ChangeVerdict.VerificationInconclusive.ToString():ChangeVerdict.Failed.ToString();
                return r.ExitCode == 0 ? "Submitted / scheduled for " + name + ". Processing completion is UNVERIFIED; check target GroupPolicy Operational logs. Current account only; RSAT uses supported remote scheduled-task mechanisms." : "Remote request failed (exit " + r.ExitCode + "). Review live errors for missing RSAT, authorization, RPC/WMI/firewall or policy restrictions. WinRM/firewall/authentication were not modified.";
            });
        }
        private void OpenSettings(string action)
        {
            if(action=="Lenovo"||action=="Dell"){
                if(Busy)return;OutputExpanded=true;
                Start("Vendor utility download",async token=>{var result=await VendorUtilityDownload.DownloadAsync(action,Queue,token).ConfigureAwait(false);Dispatch(()=>{try{if(result.Downloaded)Process.Start(UserActions.Reveal(result.Path));else OpenWeb(result.Page);}catch(Exception e){Status="File/page could not be opened: "+e.Message;}});return result.Message;});return;
            }
            try
            {
                if (action == "Check") { try { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate-action") { UseShellExecute = true }); Status = "Windows Update action requested; no scan/install completion was verified. Use Open Windows Update if this Windows build ignores the action."; return; } catch { } }
                Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true }); Status = "Windows Update Settings opened/requested. Windows manages updates and organization policy; no completed scan or installation is claimed.";
            }
            catch (Exception e) { Status = "Cannot open Windows Settings: " + e.Message + ". Check restrictions or open Settings → Windows Update manually."; }
        }
        private void RunSoftware(string action)
        {
            if (Busy) return;
            var selected = SelectedPackage;
            if (action == "Refresh") { Start("Software inventory", token => { foreach (var row in Packages) { string state = SoftwareService.Detected(row.Definition); Application.Current.Dispatcher.BeginInvoke(new Action(() => row.State = state)); } return Task.FromResult("Registry inventory refreshed. Detection alone is not proof of exact package identity, version compliance or licensing."); }); return; }
            if (action == "Repair")
            {
                if (!Confirm("Open Microsoft's App Installer page to install/update/repair WinGet? Microsoft Store and organization restrictions apply. Complete its installation there, then return and click Detect WinGet. The toolkit does not claim installation by opening a page.")) return;
                try { Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1") { UseShellExecute = true }); Status = "Microsoft App Installer page requested. Complete installation/update, then Detect WinGet."; }
                catch { OpenWeb("https://apps.microsoft.com/detail/9nblggh4nns1"); } return;
            }
            if (action == "Vendor") { if (selected != null) OpenWeb(selected.Definition.Page); return; }
            if (action == "Direct")
            {
                if (!Confirm("Download the latest x64 VS Code current-user installer directly from Microsoft's approved endpoints? Its license/privacy terms apply. The Microsoft Authenticode signature must validate before installation. Other applications use their own official direct profiles in the catalog.")) return;
                Start("Official VS Code download/install", token => new VendorDownload().InstallCodeAsync(Queue, token)); return;
            }
            if (action == "Detect") { Start("WinGet availability", token => new SoftwareService().DetectAsync(Queue, token)); return; }
            if (action == "Details" && SoftwareProvider == "Official direct")
            {
                if(selected==null)return;
                Start("Official installer metadata",async token=>{var plan=await new DirectInstaller().ResolveAsync(selected.Id,token).ConfigureAwait(false);Dispatch(()=>selected.AvailableVersion=plan.Version);return "Official source: "+plan.Url+"\r\nVersion: "+plan.Version+"\r\nExpected publisher: "+plan.Publisher+". No installer executed.";});return;
            }
            if (action == "Details")
            {
                if (selected == null || !Confirm("Query exact package metadata from the WinGet community source? This connects online and accepts the source agreement for this query. Application license details will be shown without installing.")) return;
                Start("Package details · " + selected.Id, token => new SoftwareService().DetailsAsync(selected.Id, Queue, token)); return;
            }
            if (action != "Install" && action != "Update") return;
            var batch = Packages.Where(x => x.Selected).ToArray();
            if (batch.Length == 0) { Status = "Select at least one application. Java is never selected by default; choose its distribution/version explicitly."; return; }
            if (!Confirm((action == "Update" ? "Update" : "Install") + " selected applications sequentially?\r\n" + String.Join("\r\n", batch.Select(x => x.Name + " · " + x.Id)) + "\r\nScope: " + InstallScope + ". Selected apps can install their supported prerequisites. By proceeding you accept the selected application license terms and, only if WinGet is selected, its source agreements. Direct downloads use fixed supported scopes. Use Package details/vendor page to review first. Windows requests installer elevation if needed. No automatic reboot.")) return;
            string scope = InstallScope; string provider = SoftwareProvider;
            Start("Software · " + action, async token =>
            {
                var results = new StringBuilder();
                foreach (var row in batch)
                {
                    if (token.IsCancellationRequested) { results.AppendLine(row.Name + ": skipped after cancellation request"); continue; }
                    Dispatch(() => row.State = "Running · " + action);
                    string result;
                    try {
                        if(provider == "Official direct") {
                            if(scope == "user" && row.Id != "Microsoft.VisualStudioCode" || scope == "machine" && row.Id == "Microsoft.VisualStudioCode") throw new InvalidOperationException("Direct provider scope: VS Code=current user; other catalog apps=machine. Choose default or the supported scope.");
                            string skip=await new DirectInstaller().CheckWithoutElevationAsync(row.Id,action=="Update",token).ConfigureAwait(false);
                            if(skip!=null){result=skip;Queue(new OutputChunk{Channel="status",Text=skip+"\r\n"});}
                            else if(row.Id == "Microsoft.VisualStudioCode") result = await new DirectInstaller().InstallAsync(row.Id,Queue,token,action=="Update").ConfigureAwait(false);
                            else { var r = await privilege.ExecuteAsync((action=="Update"?"Upgrade:":"Install:")+row.Id,Queue,token).ConfigureAwait(false); result = r.Summary ?? (row.Name+": Verification Inconclusive — worker returned no structured evidence"); }
                        } else result = await new SoftwareService().InstallAsync(row.Id, action == "Update", scope, Queue, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { result = row.Name + ": Cancelled before installation / at safe checkpoint"; }
                    catch (Exception e) { result = row.Name + ": Failed — " + e.Message + ". No alternative provider was executed silently."; }
                    results.AppendLine(provider == "Official direct" ? row.Name + ": result recorded above; " + (result.Split(new[]{'\r','\n'}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Review output") : result).AppendLine();
                    string state = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    Dispatch(() => row.State = state);
                }
                return results.ToString();
            });
        }
        private void OpenWeb(string page)
        { try { var uri = new Uri(page); if (uri.Scheme != "https") throw new InvalidOperationException("HTTPS required."); Process.Start(new ProcessStartInfo(page) { UseShellExecute = true }); Status = "Official page opened/requested. No download or installation was verified."; } catch (Exception e) { Status = "Cannot open official page: " + e.Message; } }
        private void ExportText()
        {
            try { var dialog = new SaveFileDialog { Filter = "Text log (*.txt)|*.txt", FileName = "IT-Toolkit-output-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" }; if (dialog.ShowDialog() == true) { File.WriteAllText(dialog.FileName, Operation + "\r\n" + Outcome + "\r\nExit: " + ExitCode + "\r\n" + Transcript(), new UTF8Encoding(false)); Status = "Text output exported; review sensitive device/event details before sharing."; } }
            catch (Exception e) { Status = "Text export failed: " + e.Message; }
        }
        private async void ExportHtml()
        {
            try
            {
                var dialog = new SaveFileDialog { Filter = "HTML report (*.html)|*.html", FileName = "IT-Toolkit-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html" };
                if (dialog.ShowDialog() == true) { var current = snapshot; var results = completed.ToArray(); string file = dialog.FileName; await Task.Run(() => Reports.WriteHtml(file, current, results)); Status = "Offline report exported. It contains inventory and completed summaries; command transcripts/event messages require separate explicit text export."; }
            }
            catch (Exception e) { Status = "Report export failed: " + e.Message; }
        }
        public bool CanClose()
        { if (Busy) { Status = "An operation is active. Use safe cancellation and wait for completion; native servicing/installers are not killed."; return false; } return true; }
        public void Dispose() { closed = true;if(diagnosticCancel!=null)diagnosticCancel.Cancel(); timer.Stop(); if (cancellation != null) cancellation.Cancel(); }
    }
}
