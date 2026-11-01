using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Toolkit.Core;

namespace Toolkit.Windows
{
    public sealed class WindowsInventoryService : IInventoryService
    {
        private readonly ProviderSlot[] slots = Enumerable.Range(0, 9).Select(x => new ProviderSlot()).ToArray();
        public async Task<InventorySnapshot> CollectAsync(CancellationToken cancellation)
        {
            string[] names = { "Windows", "Hardware", "Memory", "Storage", "Network", "Identity", "Activation", "Physical memory", "Physical drives" };
            Func<InformationSection>[] collectors = { WindowsDetails, Hardware, Memory, Storage, Network, Identity, Activation, PhysicalMemory, PhysicalDrives };
            var tasks = names.Select((n, i) => slots[i].CollectAsync(n, collectors[i], TimeSpan.FromSeconds(10), cancellation));
            return new InventorySnapshot(await Task.WhenAll(tasks).ConfigureAwait(false));
        }
        private static List<Dictionary<string, object>> Query(string query, int maximum = 64, string scope = @"\\.\root\cimv2")
        {
            var options = new EnumerationOptions { ReturnImmediately = true, Rewindable = false, Timeout = TimeSpan.FromSeconds(6) };
            var result = new List<Dictionary<string, object>>();
            using (var search = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query), options))
            using (var collection = search.Get())
            {
                foreach (ManagementObject row in collection)
                using (row)
                {
                    var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (PropertyData p in row.Properties) fields[p.Name] = p.Value;
                    result.Add(fields);
                    if (result.Count >= maximum) break;
                }
            }
            return result;
        }
        private static Dictionary<string, object> First(string query)
        { return Query(query, 1).FirstOrDefault() ?? new Dictionary<string, object>(); }
        private static object Get(Dictionary<string, object> row, string key)
        { object value; return row.TryGetValue(key, out value) ? value : null; }
        private static double? Number(Dictionary<string, object> row, string key)
        { var v = Get(row, key); return v == null ? null : (double?)Convert.ToDouble(v, CultureInfo.InvariantCulture); }
        private static InformationField F(string name, object value) { return new InformationField(name, value); }
        private static string Date(object value)
        {
            try { return value == null ? "Unavailable" : ManagementDateTimeConverter.ToDateTime((string)value).ToString("yyyy-MM-dd HH:mm:ss zzz"); }
            catch { return "Unavailable"; }
        }
        private static InformationSection WindowsDetails()
        {
            var row = First("SELECT Caption,Version,BuildNumber,OSArchitecture,InstallDate,LastBootUpTime,SystemDrive FROM Win32_OperatingSystem");
            string build = Convert.ToString(Get(row, "BuildNumber")), display = "Unavailable";
            string warning = "";
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                if (key != null) { display = Convert.ToString(key.GetValue("DisplayVersion", "Unavailable")); var ubr = key.GetValue("UBR"); if (ubr != null && build.Length > 0) build += "." + ubr; }
            }
            catch { warning = "Registry release details are unavailable."; }
            string uptime = "Unavailable";
            try { var span = DateTime.Now - ManagementDateTimeConverter.ToDateTime((string)Get(row, "LastBootUpTime")); uptime = String.Format("{0}d {1}h {2}m", span.Days, span.Hours, span.Minutes); } catch { }
            return new InformationSection("Windows", new[] { F("Edition", Get(row, "Caption")), F("Version", Get(row, "Version")), F("Display version", display), F("Build", build), F("Architecture", Get(row, "OSArchitecture")), F("Installed", Date(Get(row, "InstallDate"))), F("Last boot", Date(Get(row, "LastBootUpTime"))), F("Uptime", uptime), F("System drive", Get(row, "SystemDrive")) }, warning);
        }
        private static InformationSection Hardware()
        {
            var fields = new List<InformationField>(); var warnings = new List<string>();
            try { var r = First("SELECT Manufacturer,Model,TotalPhysicalMemory FROM Win32_ComputerSystem"); fields.Add(F("Manufacturer", Get(r, "Manufacturer"))); fields.Add(F("Model", Get(r, "Model"))); fields.Add(F("Installed RAM", Metrics.GiB(Number(r, "TotalPhysicalMemory")))); } catch { warnings.Add("Computer system unavailable."); }
            try { var rows = Query("SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor", 8); fields.Add(F("Processor", String.Join("; ", rows.Select(r => Convert.ToString(Get(r, "Name")))))); foreach (var r in rows) { fields.Add(F("CPU cores", Get(r, "NumberOfCores"))); fields.Add(F("CPU logical processors", Get(r, "NumberOfLogicalProcessors"))); } } catch { warnings.Add("CPU provider unavailable."); }
            try { var r = First("SELECT SerialNumber,SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS"); fields.Add(F("Serial number", Get(r, "SerialNumber"))); fields.Add(F("BIOS version", Get(r, "SMBIOSBIOSVersion"))); fields.Add(F("BIOS release date", Date(Get(r, "ReleaseDate")))); } catch { warnings.Add("BIOS provider unavailable."); }
            return new InformationSection("Hardware", fields, String.Join(" ", warnings));
        }
        private static InformationSection Memory()
        {
            var r = First("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem");
            double? total = Number(r, "TotalVisibleMemorySize") * 1024, free = Number(r, "FreePhysicalMemory") * 1024;
            var pct = Metrics.UsedPercent(total, free);
            return new InformationSection("Memory", new[] { F("Usable RAM", Metrics.GiB(total)), F("Used RAM", Metrics.GiB(total - free)), F("Available RAM", Metrics.GiB(free)), F("Used percent", pct.HasValue ? pct.Value.ToString("F1", CultureInfo.InvariantCulture) : null), F("Measurement", "Snapshot, not continuous monitoring") });
        }
        private static InformationSection Storage()
        {
            var rows = Query("SELECT DeviceID,VolumeName,FileSystem,Size,FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3");
            var fields = new List<InformationField>();
            string system = System.IO.Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\');
            foreach (var r in rows)
            {
                string drive = Convert.ToString(Get(r, "DeviceID")); double? size = Number(r, "Size"), free = Number(r, "FreeSpace"), pct = Metrics.UsedPercent(size, free);
                fields.Add(F(drive + " label", Get(r, "VolumeName"))); fields.Add(F(drive + " file system", Get(r, "FileSystem")));
                fields.Add(F(drive + " capacity", Metrics.GiB(size))); fields.Add(F(drive + " available", Metrics.GiB(free)));
                fields.Add(F(drive + " used percent", pct.HasValue ? pct.Value.ToString("F1", CultureInfo.InvariantCulture) : null));
                if (String.Equals(system, drive, StringComparison.OrdinalIgnoreCase))
                { fields.Add(F("System drive", drive)); fields.Add(F("System capacity", Metrics.GiB(size))); fields.Add(F("System available", Metrics.GiB(free))); fields.Add(F("System used percent", pct.HasValue ? pct.Value.ToString("F1", CultureInfo.InvariantCulture) : null)); }
            }
            return new InformationSection("Storage", fields, rows.Count == 0 ? "No local volume information returned." : null);
        }
        private static InformationSection Network()
        {
            var fields = new List<InformationField>(); var local = new List<string>(); string warning = "";
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Take(64))
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                try
                {
                    var ip = nic.GetIPProperties(); string name = nic.Name;
                    fields.Add(F(name + " description", nic.Description)); fields.Add(F(name + " state", nic.OperationalStatus));
                    fields.Add(F(name + " MAC", nic.GetPhysicalAddress().ToString()));
                    var ips = ip.UnicastAddresses.Select(x => x.Address.ToString()).ToArray();
                    fields.Add(F(name + " addresses", String.Join(", ", ips)));
                    fields.Add(F(name + " IPv4 masks", String.Join(", ", ip.UnicastAddresses.Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork).Select(x => x.IPv4Mask.ToString()))));
                    fields.Add(F(name + " gateways", String.Join(", ", ip.GatewayAddresses.Select(x => x.Address.ToString()))));
                    fields.Add(F(name + " DNS servers", String.Join(", ", ip.DnsAddresses.Select(x => x.ToString()))));
                    if (ip.GetIPv4Properties() != null) { fields.Add(F(name + " DHCP", ip.GetIPv4Properties().IsDhcpEnabled)); fields.Add(F(name + " interface index", ip.GetIPv4Properties().Index)); }
                    if (nic.OperationalStatus == OperationalStatus.Up) local.AddRange(ip.UnicastAddresses.Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork).Select(x => x.Address.ToString()));
                }
                catch { fields.Add(F(nic.Name, "Adapter details unavailable")); }
            }
            fields.Insert(0, F("Connected IPv4", String.Join(", ", local.Distinct())));
            try
            {
                var interfaces = Query("SELECT InterfaceIndex,InterfaceMetric,ConnectionState FROM MSFT_NetIPInterface WHERE AddressFamily=2", 128, @"\\.\root\StandardCimv2");
                var routes = Query("SELECT InterfaceIndex,RouteMetric,NextHop FROM MSFT_NetRoute WHERE DestinationPrefix='0.0.0.0/0'", 128, @"\\.\root\StandardCimv2");
                var candidates = (from route in routes from iface in interfaces
                    where Convert.ToInt32(Get(route, "InterfaceIndex")) == Convert.ToInt32(Get(iface, "InterfaceIndex")) && Convert.ToInt32(Get(iface, "ConnectionState")) == 1 && Convert.ToString(Get(route, "NextHop")) != "0.0.0.0"
                    select new { Address = Convert.ToString(Get(route, "NextHop")), Index = Convert.ToInt32(Get(route, "InterfaceIndex")), Metric = (Number(route, "RouteMetric") ?? 0) + (Number(iface, "InterfaceMetric") ?? 0) }).OrderBy(x => x.Metric).ThenBy(x => x.Index).ToArray();
                fields.Add(F("Default gateway", candidates.Length == 0 ? null : candidates[0].Address));
                fields.Add(F("Gateway selection", candidates.Length == 0 ? "No connected IPv4 default route returned" : "Lowest combined route/interface metric, IPv4. Snapshot; VPN target routes can differ."));
            }
            catch { fields.Add(F("Default gateway", "Unavailable")); warning = "Default-route metrics are unavailable. Configured per-adapter gateways are listed without claiming a preferred route."; }
            fields.Add(F("Connectivity", "Link/configuration only; no Internet or domain traffic was tested."));
            return new InformationSection("Network", fields, warning);
        }
        public static InformationSection Identity()
        {
            var fields = new List<InformationField>(); string warning = "";
            using (var id = WindowsIdentity.GetCurrent())
            { fields.Add(F("Current user", id.Name)); fields.Add(F("Administrator token", new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator) ? "Elevated" : "Standard user")); fields.Add(F("Account context", id.User != null && id.User.IsWellKnown(WellKnownSidType.LocalSystemSid) ? "Local system" : "Current Windows token")); }
            fields.Add(F("Computer", Environment.MachineName));
            try { var r = First("SELECT Domain,PartOfDomain,UserName FROM Win32_ComputerSystem"); fields.Add(F("Domain / workgroup", Get(r, "Domain"))); fields.Add(F("Domain joined", Get(r, "PartOfDomain"))); fields.Add(F("Interactive user", Get(r, "UserName"))); } catch { warning = "Domain/interactive-user provider unavailable."; }
            IntPtr join = IntPtr.Zero;
            try
            {
                int hr = NetGetAadJoinInformation(null, out join);
                if (hr == 0 && join != IntPtr.Zero)
                { var info = (JoinInfo)Marshal.PtrToStructure(join, typeof(JoinInfo)); fields.Add(F("Entra joined", info.JoinType == 1 ? "Yes" : "No")); fields.Add(F("Workplace registered", info.JoinType == 2 ? "Yes (current join record)" : "Not established by this record")); }
                else { fields.Add(F("Entra joined", hr == 0 ? "No join record returned" : "Unavailable")); fields.Add(F("Workplace registered", "Unavailable / no join record")); }
            }
            catch { fields.Add(F("Entra joined", "Unavailable")); fields.Add(F("Workplace registered", "Unavailable")); }
            finally { if (join != IntPtr.Zero) NetFreeAadJoinInformation(join); }
            fields.Add(F("Registration note", "Native local join record; does not validate MDM, tenant access or authentication."));
            return new InformationSection("Identity", fields, warning);
        }
        [StructLayout(LayoutKind.Sequential)] private struct JoinInfo { public int JoinType; public IntPtr JoinCertificate; }
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] private static extern int NetGetAadJoinInformation(string tenant, out IntPtr info);
        [DllImport("netapi32.dll")] private static extern void NetFreeAadJoinInformation(IntPtr info);
        private static InformationSection Activation()
        {
            var rows = Query("SELECT Name,Description,LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL", 16);
            string[] states = { "Unlicensed", "Licensed", "Out-of-box grace", "Out-of-tolerance grace", "Non-genuine grace", "Notification", "Extended grace" };
            var fields = new List<InformationField>();
            foreach (var r in rows) { int state = Convert.ToInt32(Get(r, "LicenseStatus")); fields.Add(F("Product", Get(r, "Name"))); fields.Add(F("Description", Get(r, "Description"))); fields.Add(F("Status", state >= 0 && state < states.Length ? states[state] : "Unknown")); }
            return new InformationSection("Activation", fields, rows.Count == 0 ? "No accessible Windows license record returned. Product keys are never collected." : null);
        }
        private static InformationSection PhysicalMemory()
        {
            var fields = new List<InformationField>(); var rows = Query("SELECT DeviceLocator,Manufacturer,Capacity,Speed,ConfiguredClockSpeed FROM Win32_PhysicalMemory", 64);
            foreach (var r in rows) { string slot = Convert.ToString(Get(r, "DeviceLocator")); fields.Add(F(slot + " manufacturer", Get(r, "Manufacturer"))); fields.Add(F(slot + " capacity", Metrics.GiB(Number(r, "Capacity")))); fields.Add(F(slot + " speed (MHz)", Get(r, "Speed"))); fields.Add(F(slot + " configured (MHz)", Get(r, "ConfiguredClockSpeed"))); }
            return new InformationSection("Physical memory", fields, rows.Count == 0 ? "No memory module records returned." : null);
        }
        private static InformationSection PhysicalDrives()
        {
            var fields = new List<InformationField>(); var rows = Query("SELECT Model,InterfaceType,Size FROM Win32_DiskDrive", 64);
            foreach (var r in rows) { string model = Convert.ToString(Get(r, "Model")); fields.Add(F("Model", model)); fields.Add(F(model + " interface", Get(r, "InterfaceType"))); fields.Add(F(model + " capacity", Metrics.GiB(Number(r, "Size")))); }
            return new InformationSection("Physical drives", fields, rows.Count == 0 ? "No physical drive records returned." : null);
        }
    }
}
