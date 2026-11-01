using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Toolkit.Core;
using Toolkit.Software;
using Toolkit.Reporting;

namespace Toolkit.Tests
{
    public static partial class Suite
    {
        private static int assertions;
        private static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception("FAIL: " + message); }
        private static void Throws(Action action, string message)
        { bool failed = false; try { action(); } catch { failed = true; } Check(failed, message); }
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--worker") return Toolkit.Privilege.PrivilegeService.WorkerMain(args);
            if (args.Length == 1 && args[0] == "--sleep") { ChildFixture.Sleep(); return 0; }
            if (args.Length == 1 && args[0] == "--child") { ChildFixture.Run(); return 7; }
            try
            {
                Console.WriteLine(RunCore());
                if (args.Contains("--process")) { string exe=System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName; Console.WriteLine(RunProcessFixture(exe,new[]{"--child"})); Console.WriteLine(RunTimeoutFixture(exe,new[]{"--sleep"})); Console.WriteLine(RunCancellationFixture(exe,new[]{"--sleep"})); }
                if (args.Contains("--windows")) Console.WriteLine(RunWindows());
                if (args.Contains("--probe"))
                {
                    using (var log = new JsonLogger(System.IO.Path.GetTempPath())) Console.WriteLine(new Toolkit.Privilege.PrivilegeService(log).RunAsync(CancellationToken.None).GetAwaiter().GetResult());
                }
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        public static string RunCore()
        {
            assertions = 0;
            RunBindingContracts();
            Run31Core();
            Run311Core();
            Run312Core();
            RunPublicCore();
            Check(Arguments.Host(" server.example.test ") == "server.example.test", "hostname normalization");
            Check(Arguments.Host("::1") == "::1", "IPv6 literal");
            foreach (string target in new[] { "", "https://host", "host;command", "host'", "x\r\ny", "a..b", "-host", "host-", "a b", new string('a', 64) + ".test" }) Throws(() => Arguments.Host(target), "unsafe target rejected");
            Check(Arguments.Quote(@"C:\Program Files\app.exe") == "\"C:\\Program Files\\app.exe\"", "space quoting");
            Check(Arguments.Quote("") == "\"\"", "empty argument");
            Check(Arguments.Quote("a\"b") == "\"a\\\"b\"", "embedded quote");
            Check(Arguments.Quote("C:\\a b\\") == "\"C:\\a b\\\\\"", "trailing slash");
            Throws(() => Arguments.Join(new[] { "bad\nargument" }), "control arguments rejected");
            Check(Operations.IsElevated("WorkerProbe") && !Operations.IsElevated("cmd.exe"), "privileged operation allowlist");
            Check(Toolkit.Privilege.PrivilegeService.WorkerMain(new[] { "--worker", "bad" }) == 64, "malformed worker command rejected before runtime setup");
            Throws(() => Operations.Command("PowerShell"), "arbitrary command forbidden");
            Check(Toolkit.Windows.UserActions.GroupPolicy(@"C:\Windows\System32").UseShellExecute, "interactive current-user policy window");
            Throws(()=>Operations.Command("GpUpdate"),"interactive GPUpdate command path removed");
            var terminal = new TerminalBuffer(); terminal.Append("Checking 10%"); terminal.Append("\rChecking 20%"); Check(terminal.ToString() == "Checking 20%", "CR overwrite");
            terminal.Append("\r"); terminal.Append("\nDone\n"); Check(terminal.ToString().Contains("Checking 20%" + Environment.NewLine + "Done"), "split CRLF");
            terminal.Reset(); terminal.Append("abc\bZ"); Check(terminal.ToString() == "abZ", "backspace");
            terminal.Append(new string('x', 250000)); Check(terminal.ToString().Length < 220000 && terminal.Truncated, "bounded display");
            Check(ProgressParser.Parse("[====27,5%====]") == 27.5 && ProgressParser.Parse("100%") == 100, "localized percentage");
            Check(ProgressParser.Parse("123%") == null && ProgressParser.Parse("nothing measured") == null, "no invented progress");
            Check(Metrics.UsedPercent(100, 25) == 75 && Metrics.UsedPercent(0, 0) == null && Metrics.UsedPercent(100, 101) == null, "valid resource usage");
            Check(SafeText.Redact("password=example token='private token' Authorization: Bearer private-token").IndexOf("example", StringComparison.Ordinal) < 0, "secret redaction");
            Check(!SafeText.Redact("Authorization: Bearer private-token").Contains("private-token"), "bearer redaction");
            var snapshot = new InventorySnapshot(new[] { new InformationSection("Windows", new[] { new InformationField("Edition", "<script>alert(1)</script>") }) });
            string html = Reports.Html(snapshot, new[] { "<img onerror=bad>" }); Check(!html.Contains("<script>") && html.Contains("&lt;script&gt;") && !html.Contains("<img"), "HTML injection encoding");
            Check(snapshot.Find("missing", "unknown") == "Unavailable", "missing field honesty");
            foreach (var p in Catalog.All()) { var args = Catalog.InstallArguments(p.Id, false, "default"); Check(args.Contains("--exact") && args.Contains("winget") && !args.Contains("--override"), "exact safe package arguments"); }
            Throws(() => Catalog.Find("Arbitrary.Package"), "uncataloged package forbidden");
            Throws(() => Catalog.InstallArguments("Google.Chrome", false, "arbitrary"), "scope validation");
            Check(Catalog.All().Count(x => x.Name.StartsWith("Java")) >= 4, "explicit Java variants");
            Check(InstallationVerification.Classify(0, null, null, false, false) == "Verification inconclusive", "zero exit alone is not installation proof");
            Check(InstallationVerification.Classify(0, "1", "1", true, true).StartsWith("Verification inconclusive"), "unchanged version is not update proof");
            Check(InstallationVerification.Classify(0, "1", "2", true, true).StartsWith("Update completed"), "verified version change");
            Check(InstallationVerification.Classify(3010, null, "2", true, false).StartsWith("Reboot required"), "reboot state");
            Check(InstallationVerification.Classify(5, null, "2", true, false) == "Installation failed", "failed process is not success");
            Check(VendorPolicy.IsAllowed(new Uri("https://update.code.visualstudio.com/latest/win32-x64-user/stable")), "approved official origin");
            foreach (var url in new[] { "http://update.code.visualstudio.com/a", "https://update.code.visualstudio.com.attacker.invalid/a", "https://attacker.invalid/a.exe", "https://user@update.code.visualstudio.com/a", "https://update.code.visualstudio.com:8443/a" }) Check(!VendorPolicy.IsAllowed(new Uri(url)), "unapproved origin rejected");
            var source = new SourceEvidence { Name = "winget", Type = "Microsoft.PreIndexed.Package", Arg = "https://cdn.winget.microsoft.com/cache", Identifier = "Microsoft.Winget.Source_8wekyb3d8bbwe" }; Check(source.Approved, "approved Microsoft source"); source.Arg = "https://attacker.invalid/cache"; Check(!source.Approved, "renamed malicious source rejected");
            ProtocolTests().GetAwaiter().GetResult();
            DecoderTests().GetAwaiter().GetResult();
            ProviderTests().GetAwaiter().GetResult();
            LogTests();
            return "PASS: " + assertions + " core assertions (no Windows GUI/UAC/servicing/installation certification).";
        }
        private static async Task ProtocolTests()
        {
            string session = new string('a', 32), request = new string('b', 32), nonce = new string('c', 64);
            var m = new ProbeMessage { Version = 1, Type = "Execute", Operation = "WorkerProbe", Session = session, Request = request, Nonce = nonce };
            using (var bytes = new MemoryStream())
            {
                await ProbeProtocol.WriteAsync(bytes, m, CancellationToken.None); bytes.Position = 0;
                var value = await ProbeProtocol.ReadAsync(bytes, CancellationToken.None); value.Validate("Execute", session, request, nonce); Check(value.Operation == "WorkerProbe", "framed roundtrip");
                Throws(() => value.Validate("Execute", new string('f', 32), request, nonce), "session spoof rejected");
                value.Operation = "cmd.exe"; Throws(() => value.Validate("Execute", session, request, nonce), "unknown privileged operation rejected"); value.Operation = "WorkerProbe";
                value.Percent = Double.NaN; Throws(() => value.Validate("Execute", session, request, nonce), "non-finite progress rejected");
                value.Percent = 101; Throws(() => value.Validate("Execute", session, request, nonce), "out-of-range progress rejected");
            }
            foreach (int size in new[] { 0, -1, ProbeProtocol.MaximumFrame + 1 })
            using (var bytes = new MemoryStream(BitConverter.GetBytes(size))) { bool rejected = false; try { await ProbeProtocol.ReadAsync(bytes, CancellationToken.None); } catch (InvalidDataException) { rejected = true; } Check(rejected, "invalid frame length"); }
            using (var bytes = new MemoryStream(new byte[] { 10, 0, 0, 0, 65 })) { bool rejected = false; try { await ProbeProtocol.ReadAsync(bytes, CancellationToken.None); } catch (EndOfStreamException) { rejected = true; } Check(rejected, "truncated frame rejected"); }
        }
        private sealed class ByteStream : MemoryStream
        {
            public ByteStream(byte[] data) : base(data) { }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) { return base.ReadAsync(buffer, offset, Math.Min(count, 1), token); }
        }
        private static async Task DecoderTests()
        {
            foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.GetEncoding(437) })
            {
                string text = encoding == Encoding.GetEncoding(437) ? "partial 10%\rpartial 100%\n" : "חלקי 10%\rבדיקה 100%\n€";
                var decoded = new StringBuilder();
                using (var input = new ByteStream(encoding.GetBytes(text))) await ProcessEngine.DrainAsync(input, encoding, "stdout", c => decoded.Append(c.Text), (channel, value) => new OutputChunk { Channel = channel, Text = value });
                Check(decoded.ToString() == text, "incremental decoder split byte boundaries " + encoding.WebName);
            }
            var huge = new string('a', 1200000); long seen = 0;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(huge))) await ProcessEngine.DrainAsync(stream, Encoding.UTF8, "stdout", c => seen += c.Text.Length, (channel, value) => new OutputChunk { Channel = channel, Text = value });
            Check(seen == huge.Length, "continues streaming past old 1 Mi-character cap");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("finish"))) await ProcessEngine.DrainAsync(stream, Encoding.UTF8, "stderr", c => { throw new IOException("fixture disconnected"); }, (channel, value) => new OutputChunk { Channel = channel, Text = value });
            Check(true, "observer failure does not stop drain");
        }
        private static async Task ProviderTests()
        {
            var slot = new ProviderSlot(); var blocker = new ManualResetEventSlim(false); int calls = 0;
            Func<InformationSection> blocked = () => { Interlocked.Increment(ref calls); blocker.Wait(); return new InformationSection("Fixture", new InformationField[0]); };
            var result = await slot.CollectAsync("Fixture", blocked, TimeSpan.FromMilliseconds(50), CancellationToken.None);
            Check(result.Warning.Contains("deadline"), "provider deadline is honest");
            var next = await slot.CollectAsync("Fixture", blocked, TimeSpan.FromMilliseconds(50), CancellationToken.None);
            Check(next.Warning.Contains("still finishing") && calls == 1, "hung provider cannot spawn unlimited replacements"); blocker.Set();
            using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); bool ended = false; try { await new ProviderSlot().CollectAsync("Fixture", () => { Thread.Sleep(40); return new InformationSection("Fixture", new InformationField[0]); }, TimeSpan.FromSeconds(1), cancelled.Token); } catch (OperationCanceledException) { ended = true; } Check(ended, "provider cancellation"); }
        }
        private static void LogTests()
        {
            string dir = Path.Combine(Path.GetTempPath(), "toolkit-log-test-" + Guid.NewGuid().ToString("N")); string file;
            try
            {
                using (var logger = new JsonLogger(dir)) { file = logger.Path; logger.Write("Test", "Fixture", "Completed", "password=fixture-secret"); }
                string text = File.ReadAllText(file); Check(text.Contains("Timestamp") && text.Contains("CorrelationId") && !text.Contains("fixture-secret"), "structured redacted log");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
        public static string RunProcessFixture(string executable, string[] arguments)
        {
            var events = new List<OutputChunk>(); var clock = System.Diagnostics.Stopwatch.StartNew(); double first = -1;
            var result = new ProcessEngine().RunAsync(executable, arguments, Encoding.UTF8, chunk => { lock (events) { if (first < 0 && chunk.Channel == "stdout") first = clock.Elapsed.TotalMilliseconds; events.Add(chunk); } }, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.ExitCode == 7, "actual fixture exit code");
            Check(events.Any(x => x.Channel == "stderr" && x.Text.Contains("fixture-stderr")), "actual fixture stderr");
            Check(String.Join("", events.Where(x => x.Channel == "stdout").Select(x => x.Text)).Contains("partial 100%"), "actual fixture final drain");
            Check(first >= 0 && first < result.DurationMs - 400, "actual incremental chunk before process exit");
            return "PASS: actual controlled child process, partial output/CR, stdout/stderr, pre-exit delivery and exit 7. Not a Windows servicing test.";
        }
        public static string RunCancellationFixture(string executable,string[] arguments)
        {
            using(var stop=new CancellationTokenSource())
            {
                int pid=0;var started=new ManualResetEventSlim();
                var task=new ProcessEngine().RunAsync(executable,arguments,Encoding.UTF8,c=>{if(c.Text.StartsWith("Running PID ")){pid=Int32.Parse(c.Text.Split(' ')[2].TrimEnd(';'));started.Set();}},stop.Token,true,30);
                Check(started.Wait(5000),"child reaches Running with PID");
                Throws(()=>new ProcessEngine().RunAsync(executable,arguments,Encoding.UTF8,null,CancellationToken.None,true,30).GetAwaiter().GetResult(),"duplicate process invocation blocked");
                stop.Cancel();bool cancelled=false;try{task.GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"read-only process cancellation completes");
                bool gone=false;try{using(var p=System.Diagnostics.Process.GetProcessById(pid))gone=p.HasExited;}catch(ArgumentException){gone=true;}Check(gone,"cancelled child is actually stopped");
            }
            return "PASS: actual cancellation, PID exit confirmation and duplicate process prevention.";
        }
        public static string RunTimeoutFixture(string executable, string[] arguments)
        {
            bool failed = false; var clock = System.Diagnostics.Stopwatch.StartNew();
            try { new ProcessEngine().RunAsync(executable, arguments, Encoding.UTF8, null, CancellationToken.None, true, 1).GetAwaiter().GetResult(); }
            catch (TimeoutException) { failed = true; }
            Check(failed && clock.Elapsed.TotalSeconds < 7, "read-only process timeout and cleanup");
            return "PASS: controlled read-only process deadline/cleanup. Active repair/installers are not force-stopped.";
        }
    }
    public static class ChildFixture
    {
        public static void Sleep() { Thread.Sleep(15000); }
        public static void Run()
        { Console.OutputEncoding = new UTF8Encoding(false); Console.Write("partial 10%\r"); Console.Out.Flush(); Thread.Sleep(700); Console.Error.Write("fixture-stderr"); Console.Error.Flush(); Thread.Sleep(700); Console.Write("partial 100%\n"); Console.Out.Flush(); Thread.Sleep(700); Environment.Exit(7); }
    }
}
