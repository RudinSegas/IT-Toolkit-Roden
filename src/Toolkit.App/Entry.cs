using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml;
using Toolkit.Core;
using Toolkit.Privilege;

[assembly: AssemblyTitle("IT Toolkit by Roden Segas")]
[assembly: AssemblyDescription("Portable native Windows administration toolkit")]
[assembly: AssemblyCompany("Roden Segas")]
[assembly: AssemblyCopyright("Copyright © 2026 Roden Segas. All rights reserved.")]
[assembly: AssemblyProduct("IT Toolkit by Roden Segas")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
[assembly: ComVisible(false)]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace Toolkit.App
{
    public static class Entry
    {
        public static object LoadResource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Toolkit." + name))
            {
                if (stream == null) throw new FileNotFoundException("Embedded application resource is missing: " + name);
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 }))
                    return XamlReader.Load(reader);
            }
        }
        public static void InstallBranding(Application app)
        {
            foreach(string name in new[]{"Logo","Icon"})
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Toolkit."+name))
            {
                if(stream==null)throw new FileNotFoundException("Embedded branding is missing: "+name);
                var decoder=System.Windows.Media.Imaging.BitmapDecoder.Create(stream,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var image=decoder.Frames[decoder.Frames.Count-1];
                image.Freeze();app.Resources["App"+name]=image;
            }
        }
        public static void AttachNavigation(Window window, MainViewModel vm)
        {
            string preference=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IT-Admin-Toolkit","output-height.txt");
            try{double height;if(File.Exists(preference)&&new FileInfo(preference).Length<=64&&(File.GetAttributes(preference)&FileAttributes.ReparsePoint)==0&&Double.TryParse(File.ReadAllText(preference),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out height)&&!Double.IsNaN(height)&&!Double.IsInfinity(height))vm.OutputHeight=height;}catch{}
            window.Closed+=(sender,args)=>{try{Directory.CreateDirectory(Path.GetDirectoryName(preference));File.WriteAllText(preference,vm.OutputHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));}catch{}};
            var scroll = window.FindName("PageScroll") as ScrollViewer;
            vm.PropertyChanged += (sender, e) => { if ((e.PropertyName == "CurrentPage" || e.PropertyName == "DiagnosticSection") && scroll != null) window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => scroll.ScrollToTop())); };
        }
        [STAThread]
        public static int Main(string[] args)
        {
            ILogger log = new UnavailableLogger();
            try
            {
                NativeSecurity.HardenDllSearch();
                if (args.Length > 0)
                {
                    if (args[0] == "--worker") return PrivilegeService.WorkerMain(args);
                    throw new ArgumentException("Unsupported application arguments. Start IT-Toolkit-Roden.exe normally.");
                }
                try { log = new JsonLogger(NativeSecurity.CreatePrivateLogDirectory()); } catch { }
                log.Write("Startup", "Application", "Started", "Native WPF; version " + Release.Version);
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                InstallBranding(app);
                app.Resources["NavSelection"] = new NavigationSelection();
                app.Resources.MergedDictionaries.Add((ResourceDictionary)LoadResource("Theme.xaml"));
                app.DispatcherUnhandledException += (s, e) =>
                {
                    log.Write("UnhandledUIError", "Application", "Failed", e.Exception.GetType().Name + ": " + e.Exception.Message);
                    MessageBox.Show("The action failed: " + SafeText.Redact(e.Exception.Message) + "\r\nDiagnostic log: " + log.Path + "\r\nNo security policy was changed.", Release.Title, MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true;
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => log.Write("FatalError", "Application", "Failed", (e.ExceptionObject as Exception) == null ? "Unexpected runtime failure." : ((Exception)e.ExceptionObject).GetType().Name);
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { log.Write("BackgroundError", "Application", "Failed", e.Exception.GetBaseException().Message); e.SetObserved(); };
                var window = (Window)LoadResource("MainWindow.xaml");
                window.MinWidth = Math.Min(window.MinWidth, SystemParameters.WorkArea.Width - 24);
                window.MinHeight = Math.Min(window.MinHeight, SystemParameters.WorkArea.Height - 24);
                window.Width = Math.Min(window.Width, SystemParameters.WorkArea.Width - 24);
                window.Height = Math.Min(window.Height, SystemParameters.WorkArea.Height - 24);
                using (var vm = new MainViewModel(log))
                {
                    window.DataContext = vm;
                    AttachNavigation(window, vm);
                    window.Closing += (s, e) => { if (!vm.CanClose()) e.Cancel = true; };
                    window.Loaded += (s, e) => vm.Initialize();
                    var box = window.FindName("OperationOutput") as TextBox;
                    if (box != null) box.TextChanged += (s, e) => { if (vm.AutoScroll) box.ScrollToEnd(); };
                    app.MainWindow = window;
                    app.Run(window);
                }
                log.Write("Shutdown", "Application", "Completed"); return 0;
            }
            catch (Exception e)
            {
                log.Write("StartupFailed", "Application", "Failed", e.GetType().Name + ": " + e.Message);
                MessageBox.Show("IT Toolkit by Roden Segas could not start.\r\n" + SafeText.Redact(e.Message) + "\r\nDiagnostic log: " + log.Path + "\r\nCheck .NET Framework 4.8, Windows desktop availability and application-control restrictions. No PowerShell policy changes are required.", Release.Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            finally { log.Dispose(); }
        }
    }
}
