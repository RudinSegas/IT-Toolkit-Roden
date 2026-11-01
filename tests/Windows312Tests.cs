using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Threading;
using Toolkit.App;
using Toolkit.Core;
using Toolkit.Windows;
namespace Toolkit.Tests
{
    public static partial class Suite
    {
        private static void PumpUntil(Func<bool> predicate)
        {
            var clock=Stopwatch.StartNew();
            while(!predicate()&&clock.ElapsedMilliseconds<5000)
            {
                var frame=new DispatcherFrame();
                var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(20)};
                timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
            }
            Check(predicate(),"automatic diagnostic completion within fixture deadline");
        }
        private static void Run312WindowsCollection()
        {
            var previous=SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try {
            var calls=new Dictionary<string,int>();
            var delayed=new TaskCompletionSource<DiagnosticSnapshot>();
            using(var logger=new UnavailableLogger())
            using(var vm=new MainViewModel(logger,true,(section,token)=>{
                calls[section]=calls.ContainsKey(section)?calls[section]+1:1;
                if(section=="Storage Health")return delayed.Task; // Deliberately ignore cancellation to test late-result rejection.
                return Task.FromResult(new DiagnosticSnapshot{Section=section,Summary="Fixture "+section});
            }))
            {
                vm.DiagnosticSection="Storage Health";vm.NavigateCommand.Execute("Advanced Diagnostics");
                Check(vm.DiagnosticLoading,"category entry immediately shows loading");
                PumpUntil(()=>calls.ContainsKey("Storage Health"));
                vm.DiagnosticSection="Pending Reboot";
                PumpUntil(()=>!vm.DiagnosticLoading&&vm.DiagnosticSummary=="Fixture Pending Reboot");
                delayed.SetResult(new DiagnosticSnapshot{Section="Storage Health",Summary="Stale fixture"});
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
                Check(vm.DiagnosticSummary=="Fixture Pending Reboot","late previous category cannot replace current results");
                vm.DiagnosticSection="Devices & Drivers";PumpUntil(()=>!vm.DiagnosticLoading);
                vm.DiagnosticSection="Pending Reboot";
                Check(!vm.DiagnosticLoading&&calls["Pending Reboot"]==1,"recent category reused without second collection");
            }
            } finally {SynchronizationContext.SetSynchronizationContext(previous);}
        }
    }
}
