using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Toolkit.Core
{
    [DataContract]
    public sealed class LogRecord
    {
        [DataMember] public string Timestamp;
        [DataMember] public string Level;
        [DataMember] public string Event;
        [DataMember] public string Operation;
        [DataMember] public string Outcome;
        [DataMember] public string Detail;
        [DataMember] public string CorrelationId;
        [DataMember] public double? DurationMs;
    }
    public interface ILogger : IDisposable
    {
        string Path { get; }
        string Status { get; }
        void Write(string name, string operation, string outcome, string detail = "", double? duration = null, string correlation = "");
    }
    public static class SafeText
    {
        public static string Redact(string value)
        {
            if (value == null) return "";
            value = Regex.Replace(value, "(?i)((?:password|passwd|pwd|token|secret|api[_-]?key|pidkey)\\s*[=:]\\s*)(?:\"[^\"]*\"|'[^']*'|[^\\s;,<>]+)", "$1[REDACTED]");
            value = Regex.Replace(value, "(?i)Bearer\\s+[^\\s,;]+", "Bearer [REDACTED]");
            value = Regex.Replace(value, "\\b[A-Z0-9]{5}(?:-[A-Z0-9]{5}){4}\\b", "[REDACTED-LICENSE]");
            return value.Length > 2048 ? value.Substring(0, 2048) + " [truncated]" : value;
        }
    }
    public sealed class JsonLogger : ILogger
    {
        private readonly BlockingCollection<LogRecord> queue = new BlockingCollection<LogRecord>(256);
        private readonly Task writer;
        private volatile string status = "Available";
        private volatile bool closed;
        public string Path { get; private set; }
        public string Status { get { return status; } }
        public JsonLogger(string directory)
        {
            Directory.CreateDirectory(directory);
            Path = System.IO.Path.Combine(directory, "Toolkit-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".jsonl");
            writer = Task.Run((Action)Drain);
        }
        public void Write(string name, string operation, string outcome, string detail = "", double? duration = null, string correlation = "")
        {
            if (closed) return;
            var record = new LogRecord { Timestamp = DateTimeOffset.Now.ToString("o"), Level = outcome == "Failed" ? "Error" : "Information", Event = name, Operation = operation, Outcome = outcome, Detail = SafeText.Redact(detail), DurationMs = duration, CorrelationId = correlation };
            try { if (!queue.TryAdd(record)) status = "Log queue full; some records were dropped."; }
            catch (InvalidOperationException) { }
        }
        private void Drain()
        {
            try
            {
                long bytes = 0;
                using (var output = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                foreach (var record in queue.GetConsumingEnumerable())
                {
                    using (var memory = new MemoryStream())
                    {
                        new DataContractJsonSerializer(typeof(LogRecord)).WriteObject(memory, record);
                        var data = memory.ToArray();
                        if (bytes + data.Length + 1 > 2 * 1024 * 1024) { status = "Session log limit reached (2 MiB)."; continue; }
                        output.Write(data, 0, data.Length); output.WriteByte(10); output.Flush(); bytes += data.Length + 1;
                    }
                }
            }
            catch (Exception e) { status = "Logging unavailable (" + e.GetType().Name + ")."; }
        }
        public void Dispose()
        {
            if (closed) return; closed = true; queue.CompleteAdding();
            // Never wait indefinitely on an unavailable profile or disk.
            writer.Wait(1500);
        }
    }
    public sealed class UnavailableLogger : ILogger
    {
        public string Path { get { return "Unavailable"; } }
        public string Status { get { return "Logging unavailable. Check access to the local profile."; } }
        public void Write(string name, string operation, string outcome, string detail = "", double? duration = null, string correlation = "") { }
        public void Dispose() { }
    }
}
