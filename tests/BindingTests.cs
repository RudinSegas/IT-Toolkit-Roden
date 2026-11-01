using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace Toolkit.Tests
{
    public static partial class Suite
    {
        // Static resource contract, executable on the development host without loading WPF.
        private static void CheckBindingResource(string xml)
        {
            var editable = new HashSet<string>("NetworkTarget TcpPort Timeout ServiceSearch ServiceStatus EventLog EventSeverity EventDays EventMaximum EnterpriseEnabled RemoteTarget SelectedPackage Selected InstallScope AutoScroll IsDropDownOpen DiagnosticSection SelectedAdapter DiagnosticSearch ProblemDevicesOnly RedactReports InventoryScope SoftwareProvider ServiceToRestart Included SelectedDiagnostic SelectedInventory OutputExpanded OutputHeight SelectedService SelectedEvent EventIdFilter EventSourceFilter".Split(' '));
            var document = new XmlDocument(); document.XmlResolver = null; document.LoadXml(xml);
            foreach (XmlElement element in document.SelectNodes("//*"))
            {
                foreach (XmlAttribute attribute in element.Attributes)
                {
                    if (!attribute.Value.StartsWith("{Binding", StringComparison.Ordinal)) continue;
                    string prefix = attribute.Value.Substring(8).Split('{')[0];
                    var mode = Regex.Match(prefix, @"\bMode=(OneWay|OneTime|TwoWay|OneWayToSource)\b");
                    if (!mode.Success) throw new Exception("Binding requires explicit direction: " + element.LocalName + "." + attribute.Name);
                    string path = Regex.Match(prefix, @"^\s*([\w.]+)").Groups[1].Value;
                    bool writes = mode.Value == "Mode=TwoWay" || mode.Value == "Mode=OneWayToSource";
                    if (writes && !editable.Contains(path)) throw new Exception("Writeback to display property: " + path);
                    if (editable.Contains(path) && mode.Value != "Mode=TwoWay" && (attribute.Name=="SelectedItem" || attribute.Name=="IsChecked" || attribute.Name=="IsExpanded" || attribute.Name=="Value" || attribute.Name=="Text" && element.LocalName=="TextBox")) throw new Exception("Editable binding must remain TwoWay: " + path);
                    if (path == "Progress" && mode.Value != "Mode=OneWay") throw new Exception("Progress must keep live OneWay notifications.");
                }
                if ((element.LocalName == "Binding" || element.LocalName == "MultiBinding") && element.GetAttribute("Mode") != "OneWay")
                    throw new Exception("Display/theme binding requires OneWay.");
            }
        }
        private static void RunBindingContracts()
        {
            foreach (string name in new[] { "MainWindow.xaml", "Theme.xaml" })
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Toolkit." + name))
            using (var reader = new StreamReader(stream))
            { CheckBindingResource(reader.ReadToEnd()); Check(true, "explicit binding modes: " + name); }
            Throws(() => CheckBindingResource("<ProgressBar Value='{Binding Progress}'/>"), "regression: implicit progress binding rejected");
            Throws(() => CheckBindingResource("<ProgressBar Value='{Binding Progress, Mode=TwoWay}'/>"), "regression: writable progress binding rejected");
            Throws(() => CheckBindingResource("<TextBox Text='{Binding Output, Mode=TwoWay}'/>"), "regression: output writeback rejected");
            Throws(() => CheckBindingResource("<Slider Value='{Binding Progress, Mode=OneTime}'/>"), "regression: frozen progress rejected");
        }
    }
}
