using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Toolkit.Core;

namespace Toolkit.Windows
{
    public sealed class DiagnosticResult
    {
        public string Status;
        public string Text;
        public DiagnosticResult(string status, string text) { Status = status; Text = text; }
    }
    public sealed class NetworkDiagnostics
    {
        private static void ObserveFault(Task task) { task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private static async Task<T> Bounded<T>(Task<T> task, int timeout, CancellationToken token)
        {
            if (await Task.WhenAny(task, Task.Delay(timeout, token)).ConfigureAwait(false) != task)
            { ObserveFault(task); token.ThrowIfCancellationRequested(); throw new TimeoutException("Network operation timed out."); }
            return await task.ConfigureAwait(false);
        }
        public async Task<DiagnosticResult> RunAsync(string operation, string target, int port, int timeout, CancellationToken token)
        {
            if (timeout < 250 || timeout > 30000) throw new ArgumentException("Timeout must be 250–30000 ms.");
            if (operation == "Configuration") return new DiagnosticResult("Collected", await Task.Run((Func<string>)Configuration, token).ConfigureAwait(false));
            if (operation == "Gateway")
            {
                var gateway = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().GatewayAddresses).Select(g => g.Address).Where(a => !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any)).Distinct().ToArray();
                if (gateway.Length == 0) throw new InvalidOperationException("No configured gateway on an active adapter.");
                var text = new StringBuilder("Configured gateways are tested independently; no claim about VPN/default route selection.\r\n"); bool any = false;
                foreach (var g in gateway) { var r = await RunAsync("Ping", g.ToString(), port, timeout, token).ConfigureAwait(false); text.AppendLine(r.Text); any |= r.Status == "Success"; }
                return new DiagnosticResult(any ? "Success" : "Warning", text.ToString());
            }
            if (operation == "Domain")
            {
                string domain;
                using (var search = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"), new ObjectQuery("SELECT Domain,PartOfDomain FROM Win32_ComputerSystem"), new EnumerationOptions { ReturnImmediately = true, Rewindable = false, Timeout = TimeSpan.FromSeconds(6) }))
                using (var values = search.Get())
                { using (var row = values.Cast<ManagementObject>().FirstOrDefault()) { if (row == null || !Convert.ToBoolean(row["PartOfDomain"])) return new DiagnosticResult("Not applicable", "This computer is not joined to an Active Directory domain. Entra join alone does not provide an AD controller."); domain = Convert.ToString(row["Domain"]); } }
                // DsGetDcName is a supported DC locator and honors the computer's current domain context.
                var controller = await Bounded(Task.Run(() => DomainController(domain)), timeout, token).ConfigureAwait(false);
                var tcp = await RunAsync("Tcp", controller, 389, timeout, token).ConfigureAwait(false);
                return new DiagnosticResult(tcp.Status, "AD domain: " + domain + "\r\nController: " + controller + "\r\n" + tcp.Text + "\r\nLDAP port reachability does not prove Kerberos, logon or secure-channel health.");
            }
            target = Arguments.Host(target);
            if (operation == "ReverseDns")
            {
                var entry = await Bounded(Dns.GetHostEntryAsync(target), timeout, token).ConfigureAwait(false);
                return new DiagnosticResult("Resolved", "Target: " + target + "\r\nName: " + entry.HostName + "\r\n" + String.Join("\r\n", entry.AddressList.Select(x => x.ToString())));
            }
            var dnsWatch = System.Diagnostics.Stopwatch.StartNew();
            IPAddress literal;
            var addresses = IPAddress.TryParse(target, out literal) ? new[] { literal } : await Bounded(Dns.GetHostAddressesAsync(target), timeout, token).ConfigureAwait(false);
            if (addresses.Length == 0) throw new InvalidOperationException("No addresses returned by the Windows system resolver.");
            if (operation == "Dns") return new DiagnosticResult("Resolved", target + "\r\n" + String.Join("\r\n", addresses.Select(x => x.AddressFamily + ": " + x)) + "\r\nDNS resolution elapsed: " + dnsWatch.ElapsedMilliseconds + " ms.\r\nSystem resolver: hosts/cache/configured DNS. IP literals are returned unchanged.");
            if (operation == "Ping")
            {
                var ip = addresses.OrderBy(x => x.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).First();
                var text = new StringBuilder("Target: " + target + " (" + ip + ")\r\n"); int success = 0;
                using (var ping = new Ping())
                for (int i = 1; i <= 4; i++)
                { token.ThrowIfCancellationRequested(); var reply = await ping.SendPingAsync(ip, timeout).ConfigureAwait(false); if (reply.Status == IPStatus.Success) success++; text.AppendLine("Attempt " + i + ": " + reply.Status + (reply.Status == IPStatus.Success ? " · " + reply.RoundtripTime + " ms" : "")); }
                text.AppendLine("Packet loss: " + ((4-success)*25) + "% · Received: " + success + "/4. ICMP can be filtered; failure does not establish that a computer is offline.");
                return new DiagnosticResult(success == 4 ? "Success" : "Warning", text.ToString());
            }
            if (operation != "Tcp") throw new ArgumentException("Unknown network diagnostic.");
            if (port < 1 || port > 65535) throw new ArgumentException("TCP port must be 1–65535.");
            var detail = new StringBuilder(); var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var address in addresses)
            using (var client = new TcpClient(address.AddressFamily))
            {
                token.ThrowIfCancellationRequested(); int remaining = timeout - (int)clock.ElapsedMilliseconds; if (remaining <= 0) break;
                try { await Bounded(client.ConnectAsync(address, port).ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }), remaining, token).ConfigureAwait(false); return new DiagnosticResult("Success", "TCP handshake accepted: " + address + ":" + port + "\r\nDoes not validate TLS, the application protocol or authentication."); }
                catch (OperationCanceledException) { throw; } catch (Exception e) { detail.AppendLine(address + ": " + e.GetType().Name); }
            }
            return new DiagnosticResult("Failure", "No TCP connection to " + target + ":" + port + "\r\n" + detail);
        }
        private static string Configuration()
        {
            var text = new StringBuilder();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                text.AppendLine(nic.Name + " · " + nic.OperationalStatus + " · " + nic.Description);
                try { var ip = nic.GetIPProperties(); text.AppendLine("Addresses: " + String.Join(", ", ip.UnicastAddresses.Select(x => x.Address.ToString()))); text.AppendLine("DNS: " + String.Join(", ", ip.DnsAddresses)); text.AppendLine("Gateways: " + String.Join(", ", ip.GatewayAddresses.Select(x => x.Address))); } catch { text.AppendLine("Details unavailable."); }
                text.AppendLine();
            }
            return text.ToString();
        }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct DcInfo { public string ControllerName, ControllerAddress; public uint AddressType; public Guid DomainGuid; public string DomainName, ForestName; public uint Flags; public string DcSite, ClientSite; }
        [System.Runtime.InteropServices.DllImport("netapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int DsGetDcName(string computer, string domain, IntPtr guid, string site, uint flags, out IntPtr info);
        [System.Runtime.InteropServices.DllImport("netapi32.dll")] private static extern int NetApiBufferFree(IntPtr info);
        private static string DomainController(string domain)
        {
            IntPtr buffer; int error = DsGetDcName(null, domain, IntPtr.Zero, null, 0x40000010, out buffer);
            if (error != 0) throw new System.ComponentModel.Win32Exception(error);
            try { return ((DcInfo)System.Runtime.InteropServices.Marshal.PtrToStructure(buffer, typeof(DcInfo))).ControllerName.TrimStart('\\'); }
            finally { NetApiBufferFree(buffer); }
        }
    }
    public sealed class SystemDiagnostics
    {
        public DiagnosticResult Services(string search, string status, CancellationToken token = default(CancellationToken))
        {
            if (status != "All" && status != "Running" && status != "Stopped") throw new ArgumentException("Unknown service status.");
            if ((search ?? "").Length > 100) throw new ArgumentException("Search is limited to 100 characters.");
            var text = new StringBuilder(); int count = 0;
            using (var query = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"), new ObjectQuery("SELECT Name,DisplayName,State,StartMode FROM Win32_Service"), new EnumerationOptions { ReturnImmediately = true, Rewindable = false, Timeout = TimeSpan.FromSeconds(10) }))
            using (var rows = query.Get())
            foreach (ManagementObject row in rows)
            using (row)
            { token.ThrowIfCancellationRequested(); string name = Convert.ToString(row["Name"]), display = Convert.ToString(row["DisplayName"]), state = Convert.ToString(row["State"]); if ((status == "All" || status == state) && (name.IndexOf(search ?? "", StringComparison.OrdinalIgnoreCase) >= 0 || display.IndexOf(search ?? "", StringComparison.OrdinalIgnoreCase) >= 0)) { text.AppendLine(display + " [" + name + "]\r\nState: " + state + " · Startup: " + row["StartMode"] + "\r\n"); count++; } }
            return new DiagnosticResult("Collected", "Matching services: " + count + "\r\nRead-only; no service start/stop operations.\r\n\r\n" + text);
        }
        public DiagnosticResult Events(string log, string severity, int days, int maximum, CancellationToken token = default(CancellationToken))
        {
            if (log != "System" && log != "Application") throw new ArgumentException("Unsupported event log.");
            if (severity != "Both" && severity != "Error" && severity != "Warning") throw new ArgumentException("Unsupported severity.");
            if (days < 1 || days > 30 || maximum < 1 || maximum > 500) throw new ArgumentException("Event range is 1–30 days, 1–500 records.");
            string levels = severity == "Error" ? "Level=2" : severity == "Warning" ? "Level=3" : "Level=2 or Level=3";
            string query = "*[System[(" + levels + ") and TimeCreated[timediff(@SystemTime) <= " + ((long)days * 86400000).ToString(CultureInfo.InvariantCulture) + "]]]";
            var text = new StringBuilder(); int count = 0;
            using (var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, query) { ReverseDirection = true }))
            {
                EventRecord record;
                while (count < maximum && (record = reader.ReadEvent(TimeSpan.FromSeconds(5))) != null)
                using (record)
                {
                    token.ThrowIfCancellationRequested();
                    string message; try { message = record.FormatDescription(); } catch { message = "Localized message resource unavailable."; }
                    if ((message ?? "").Length > 12000) message = message.Substring(0, 12000) + " [truncated]";
                    text.AppendLine(record.TimeCreated + " · " + record.LevelDisplayName + " · " + record.Id + " · " + record.ProviderName); text.AppendLine(message); text.AppendLine(); count++;
                }
            }
            return new DiagnosticResult("Collected", log + " · " + count + " error/warning records, last " + days + " days. Event messages can contain personal information; review before export.\r\n\r\n" + text);
        }
    }
}
