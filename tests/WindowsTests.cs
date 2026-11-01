using System;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Text;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Toolkit.App;
using Toolkit.Core;
using Toolkit.Privilege;

namespace Toolkit.Tests
{
    public static partial class Suite
    {
        private sealed class BindingErrors : TraceListener
        {
            public readonly StringBuilder Messages = new StringBuilder();
            private readonly SourceLevels previous;
            public BindingErrors()
            {
                previous = PresentationTraceSources.DataBindingSource.Switch.Level;
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
                PresentationTraceSources.DataBindingSource.Listeners.Add(this);
            }
            public override void Write(string message) { Messages.Append(message); }
            public override void WriteLine(string message) { Messages.AppendLine(message); }
            protected override void Dispose(bool disposing)
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
                PresentationTraceSources.DataBindingSource.Switch.Level = previous;
                base.Dispose(disposing);
            }
        }
        // Explicit Windows-only acceptance fixtures: read-only UI and harmless NTFS/pipe files.
        public static string RunWindows()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) throw new PlatformNotSupportedException("Windows tests require Windows x64/.NET Framework 4.8.");
            Run31WindowsMetadata();
            Run311WindowsFiles();
            using (var bindingErrors = new BindingErrors())
            {
            var app = Application.Current ?? new Application(); Entry.InstallBranding(app); app.Resources["NavSelection"] = new NavigationSelection(); app.Resources.MergedDictionaries.Add((ResourceDictionary)Entry.LoadResource("Theme.xaml"));
            using (var logger = new UnavailableLogger())
            using (var vm = new MainViewModel(logger,false))
            {
                vm.Cards.Add(new DashboardCard { Name = "Fixture", Fields = new[] { new InformationField("Fixture", "Read-only value") }, HasUsage = true, Usage = 42 });
                vm.Sections.Add(new InformationSection("Fixture", new[] { new InformationField("Fixture", "Read-only value") }));
                foreach (string name in "NetworkTarget TcpPort Timeout ServiceSearch ServiceStatus EventLog EventSeverity EventDays EventMaximum EnterpriseEnabled RemoteTarget SelectedPackage InstallScope AutoScroll".Split(' '))
                    Check(typeof(MainViewModel).GetProperty(name).GetSetMethod(false) != null, "Editable binding has a public setter: " + name);
                Check(typeof(PackageRow).GetProperty("Selected").GetSetMethod(false) != null, "Package selection remains editable");
                var window = (Window)Entry.LoadResource("MainWindow.xaml"); window.DataContext = vm; Entry.AttachNavigation(window,vm); window.Show();
                foreach (var page in new[] { "Advanced Diagnostics", "Dashboard", "System Information", "Network Diagnostics", "Services & Events", "Windows Maintenance", "Windows Update", "Software", "Reports", "Settings / About" })
                { vm.NavigateCommand.Execute(page); window.UpdateLayout(); window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { })); Check(vm.PageTitle == page, "WPF page materialization " + page); }
                vm.NavigateCommand.Execute("Advanced Diagnostics");
                foreach(string category in vm.DiagnosticSections){vm.DiagnosticSection=category;window.UpdateLayout();window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));Check(vm.IsNetworkDiagnostic==(category=="Network Diagnostics+"),"category context visibility "+category);}
                vm.OutputExpanded=true;vm.OutputHeight=240;window.UpdateLayout();window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
                Check(((TextBox)window.FindName("OperationOutput")).ActualHeight==240,"output panel preferred height binding");vm.OutputExpanded=false;
                var scroll = (ScrollViewer)window.FindName("PageScroll");
                vm.NavigateCommand.Execute("Advanced Diagnostics"); window.UpdateLayout();
                window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
                scroll.ScrollToEnd(); window.UpdateLayout();
                vm.NavigateCommand.Execute("Reports"); window.UpdateLayout();
                window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
                Check(scroll.VerticalOffset==0,"Navigation resets scroll to top");
                var progress = typeof(MainViewModel).GetProperty("Progress");
                Check(progress.GetSetMethod(false) == null, "Progress has no public setter");
                bool notified = false;
                vm.PropertyChanged += (sender, e) => { if (e.PropertyName == "Progress") notified = true; };
                var bar = (ProgressBar)window.FindName("OperationProgress");
                foreach (double value in new[] { 17.0, 63.0 })
                {
                    notified = false;
                    progress.GetSetMethod(true).Invoke(vm, new object[] { value });
                    window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                    Check(notified && bar.Value == value, "Progress notification reaches the actual WPF bar");
                }
                Check(bindingErrors.Messages.Length == 0, "WPF binding errors: " + bindingErrors.Messages);
                window.Close();
            }
            }
            RunPublicWindowsMetadata();
            Run312WindowsCollection();
            string dir = Path.Combine(Path.GetTempPath(), "Toolkit-guard-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            string image = Path.Combine(dir, "renamed IT-Toolkit-Roden.exe"); File.WriteAllText(image, "harmless fixture, never executed");
            using (var config = Assembly.GetExecutingAssembly().GetManifestResourceStream("Toolkit.App.config")) using (var dest = File.Create(image + ".config")) config.CopyTo(dest);
            try
            {
                using (var guard = new ImageLease(image))
                { Throws(() => File.WriteAllText(image, "mutated"), "NTFS image write blocked"); Throws(() => File.Delete(image), "NTFS image delete blocked"); Throws(() => File.WriteAllText(image + ".config", "mutated"), "runtime config write blocked"); Throws(() => Directory.Move(dir, dir + "-moved"), "namespace rename blocked"); }
                File.WriteAllText(image, "released"); Check(File.ReadAllText(image) == "released", "lease release");
            }
            finally { File.Delete(image); File.Delete(image + ".config"); Directory.Delete(dir); }
            string session = Guid.NewGuid().ToString("N");
            using (var server = (NamedPipeServerStream)typeof(PrivilegeService).GetMethod("CreatePipe", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { session }))
            {
                var connected = Task.Factory.FromAsync(server.BeginWaitForConnection, server.EndWaitForConnection, null);
                using (var client = (NamedPipeClientStream)typeof(PrivilegeService).GetMethod("ConnectClient", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { session }))
                { Check(connected.Wait(3000), "Windows pipe connects with least data rights"); uint pid; Check(NativeSecurity.GetNamedPipeClientProcessId(server.SafePipeHandle, out pid) && pid == System.Diagnostics.Process.GetCurrentProcess().Id, "native client identity"); }
            }
            return "PASS: Windows WPF page/layout fixture, NTFS namespace/image/config guards, native pipe rights/identity. Real UAC/servicing/installations require separate explicit acceptance.";
        }
    }
}
