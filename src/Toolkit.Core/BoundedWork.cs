using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
namespace Toolkit.Core
{
    public static class BoundedWork
    {
        private static readonly ConcurrentDictionary<string,Task> pending=new ConcurrentDictionary<string,Task>();
        public static async Task<T> Run<T>(string key,Func<Task<T>> work,int seconds,CancellationToken token)
        {
            var gate=new TaskCompletionSource<bool>();if(!pending.TryAdd(key,gate.Task))throw new InvalidOperationException("The previous "+key+" provider is still finishing. Retry later.");
            Task<T> task;
            try {task=Task.Run(work);}catch{Task removed;pending.TryRemove(key,out removed);throw;}
            var cleanup = task.ContinueWith(t=>{var ignored=t.Exception;Task removed;pending.TryRemove(key,out removed);},TaskScheduler.Default);
            if(await Task.WhenAny(task,Task.Delay(TimeSpan.FromSeconds(seconds),token)).ConfigureAwait(false)!=task)
            {token.ThrowIfCancellationRequested();throw new TimeoutException(key+" collection deadline exceeded. Provider is still finishing; duplicate requests are blocked. No success is claimed.");}
            return await task.ConfigureAwait(false);
        }
    }
    public interface ISpeedtestProvider
    {
        string Name {get;}
        bool SupportsNativeExecution {get;}
        string Availability {get;}
    }
    public sealed class OoklaWebsiteProvider:ISpeedtestProvider
    {
        public string Name {get{return "Ookla Speedtest.net (browser)";}}
        public bool SupportsNativeExecution {get{return false;}}
        public string Availability {get{return "CLI redistribution/enterprise permission was not established. Browser handoff only; results are not captured or fabricated. Review provider terms; tests consume bandwidth.";}}
    }
}
