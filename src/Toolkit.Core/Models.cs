using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Toolkit.Core
{
    public static class Release
    {
        public const string Version = "1.0.0";
        public const string Title = "IT Toolkit by Roden Segas";
    }
    public sealed class InformationField
    {
        public string Name { get; private set; }
        public string Value { get; private set; }
        public InformationField(string name, object value)
        {
            Name = name;
            Value = value == null || String.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture))
                ? "Unavailable" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
    public sealed class InformationSection
    {
        public string Name { get; private set; }
        public InformationField[] Fields { get; private set; }
        public string Warning { get; private set; }
        public InformationSection(string name, IEnumerable<InformationField> fields, string warning = null)
        { Name = name; Fields = fields.ToArray(); Warning = warning ?? ""; }
        public string Find(string name) { var f = Fields.FirstOrDefault(x => x.Name == name); return f == null ? "Unavailable" : f.Value; }
        public override string ToString()
        { return Name + Environment.NewLine + String.Join(Environment.NewLine, Fields.Select(x => x.Name + ": " + x.Value)) + (Warning.Length == 0 ? "" : Environment.NewLine + "Warning: " + Warning); }
    }
    public sealed class InventorySnapshot
    {
        public DateTimeOffset CollectedAt { get; private set; }
        public InformationSection[] Sections { get; private set; }
        public InventorySnapshot(IEnumerable<InformationSection> sections)
        { Sections = sections.ToArray(); CollectedAt = DateTimeOffset.Now; }
        public string Find(string section, string name)
        { var s = Sections.FirstOrDefault(x => x.Name == section); return s == null ? "Unavailable" : s.Find(name); }
        public string Warnings { get { return String.Join(Environment.NewLine, Sections.Where(x => x.Warning.Length > 0).Select(x => x.Name + ": " + x.Warning)); } }
        public override string ToString()
        { return Release.Title + " " + Release.Version + Environment.NewLine + "Collected: " + CollectedAt.ToString("o") + Environment.NewLine + Environment.NewLine + String.Join(Environment.NewLine + Environment.NewLine, Sections.Select(x => x.ToString())); }
    }
    public interface IInventoryService { Task<InventorySnapshot> CollectAsync(CancellationToken cancellation); }
    public interface IPrivilegeProbe { Task<string> RunAsync(CancellationToken cancellation); }
    public static class Metrics
    {
        public static double? UsedPercent(double? total, double? free)
        { return !total.HasValue || !free.HasValue || total <= 0 || free < 0 || free > total ? null : (double?)Math.Round(100 * (1 - free.Value / total.Value), 1); }
        public static string GiB(double? bytes) { return !bytes.HasValue || bytes < 0 ? "Unavailable" : (bytes.Value / 1073741824d).ToString("N1", CultureInfo.InvariantCulture) + " GB"; }
    }
    // Typed execution data is independent of WPF and transport.
    public enum OperationState { Idle, Starting, Cancelling, TimedOut, Running, Completed, Verified, Inconclusive, Failed, Cancelled, Blocked }
    public sealed class OutputChunk
    {
        public long Sequence { get; set; }
        public string Channel { get; set; }
        public string Text { get; set; }
        public double? MeasuredPercent { get; set; }
    }
    public interface IOutputObserver { void OnOutput(OutputChunk chunk); }

    // One outstanding provider call per slot, including calls that outlive the UI deadline.
    // Cancellation never aborts a WMI thread; late faults are observed and no replacement flood is started.
    public sealed class ProviderSlot
    {
        private readonly object gate = new object();
        private Task<InformationSection> pending;
        public async Task<InformationSection> CollectAsync(string name, Func<InformationSection> collect, TimeSpan timeout, CancellationToken token)
        {
            Task<InformationSection> task;
            lock (gate)
            {
                if (pending != null && !pending.IsCompleted)
                    return new InformationSection(name, new InformationField[0], "Previous provider call is still finishing. Refresh later.");
                pending = Task.Run(collect);
                task = pending;
                task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
            var deadline = Task.Delay(timeout, token);
            if (await Task.WhenAny(task, deadline).ConfigureAwait(false) != task)
            {
                token.ThrowIfCancellationRequested();
                return new InformationSection(name, new InformationField[0], "Collection deadline exceeded; provider continues in background. No system change was made.");
            }
            token.ThrowIfCancellationRequested();
            try { return await task.ConfigureAwait(false); }
            catch (Exception e) { return new InformationSection(name, new InformationField[0], "Information is unavailable or access is restricted (" + e.GetType().Name + ")."); }
        }
    }
}
