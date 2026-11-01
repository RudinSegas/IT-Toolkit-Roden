using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Toolkit.Core;
using Toolkit.Windows;
namespace Toolkit.App
{
    public sealed partial class MainViewModel
    {
        public ObservableCollection<ServiceRow> ServiceRows{get;private set;}
        public ObservableCollection<EventRow> EventRows{get;private set;}
        private List<ServiceRow> allServices=new List<ServiceRow>();
        private ServiceRow selectedService;
        public ServiceRow SelectedService{get{return selectedService;}set{selectedService=value;Changed("SelectedService");ServiceActionStatus=value==null?"Select a service.":value.Explanation;Changed("CanStartService");Changed("CanStopService");Changed("CanRestartService");}}
        public bool CanStartService{get{return !Busy&&SelectedService!=null&&SelectedService.CanStart;}}
        public bool CanStopService{get{return !Busy&&SelectedService!=null&&SelectedService.CanStop;}}
        public bool CanRestartService{get{return !Busy&&SelectedService!=null&&SelectedService.CanRestart;}}
        private string serviceActionStatus="Select a service.",serviceListStatus="",eventListStatus="Choose one log and click Load events.",eventDetails="Select an event to read its complete message.";
        public string ServiceActionStatus{get{return serviceActionStatus;}private set{serviceActionStatus=value;Changed("ServiceActionStatus");}}
        public string ServiceListStatus{get{return serviceListStatus;}private set{serviceListStatus=value;Changed("ServiceListStatus");}}
        public string EventListStatus{get{return eventListStatus;}private set{eventListStatus=value;Changed("EventListStatus");}}
        public string EventDetails{get{return eventDetails;}private set{eventDetails=value;Changed("EventDetails");}}
        public string[] AvailableEventLogs{get{return ServiceEventBrowser.Logs;}}
        public string[] AvailableEventSeverities{get{return new[]{"Both","Critical","Error","Warning","Information","All"};}}
        public string EventIdFilter{get;set;}public string EventSourceFilter{get;set;}
        private EventRow selectedEvent;private long detailGeneration;private string eventQueryKey;private long nextEventId;
        public EventRow SelectedEvent{get{return selectedEvent;}set{selectedEvent=value;Changed("SelectedEvent");LoadEventDetails(value);}}
        public ICommand ServiceActionCommand{get;private set;}public ICommand NextEventsCommand{get;private set;}public ICommand CopyEventCommand{get;private set;}
        private bool serviceLoading,eventLoading;
        private void InitializeBrowsers()
        {
            ServiceRows=new ObservableCollection<ServiceRow>();EventRows=new ObservableCollection<EventRow>();
            ServiceActionCommand=new Command(p=>OperateService(Convert.ToString(p)),()=>!Busy&&SelectedService!=null);
            NextEventsCommand=new Command(p=>LoadEvents(true),()=>!eventLoading&&nextEventId>0);
            CopyEventCommand=new Command(p=>Copy(EventDetails));
        }
        private void FilterServices(){if(ServiceRows==null)return;ServiceRows.Clear();foreach(var row in allServices.Where(x=>(ServiceStatus=="All"||x.Status==ServiceStatus)&&(x.Name+" "+x.DisplayName).IndexOf(ServiceSearch??"",StringComparison.OrdinalIgnoreCase)>=0))ServiceRows.Add(row);}
        private async void LoadServices()
        {
            if(serviceLoading||closed)return;serviceLoading=true;ServiceListStatus="Loading services…";string selected=SelectedService==null?null:SelectedService.Name;
            try{allServices=await BoundedWork.Run("Helpdesk services",()=>Task.FromResult(ServiceEventBrowser.Services(CancellationToken.None)),30,CancellationToken.None);if(closed)return;FilterServices();SelectedService=ServiceRows.FirstOrDefault(x=>x.Name==selected);ServiceListStatus=allServices.Count+" services · "+DateTime.Now.ToString("HH:mm:ss");}
            catch(Exception e){ServiceListStatus="Not Available — "+e.Message;}finally{serviceLoading=false;}
        }
        private void OperateService(string verb)
        {
            var row=SelectedService;if(row==null||Busy||!new[]{"StartService","StopService","RestartService"}.Contains(verb))return;
            if(verb=="StartService"&&!row.CanStart||verb=="StopService"&&!row.CanStop||verb=="RestartService"&&!row.CanRestart){ServiceActionStatus=row.Explanation;return;}
            if(!Confirm(verb.Replace("Service","")+" "+row.DisplayName+" ("+row.Name+")? This can interrupt applications, network connections or Windows operations. Windows service permissions remain authoritative."))return;
            Start(verb+":"+row.Name,async token=>{
                string message;
                try{
                    string before=await Task.Run(()=>QuickRepair.ReadServiceState(row.Name)).ConfigureAwait(false);string desired=verb=="StopService"?"Stopped":"Running";
                    if(before==desired&&verb!="RestartService"){operationVerdict=ChangeVerdict.AlreadyConfigured.ToString();message="Already "+desired+" — No Changes Required; no elevation.";}
                    else {var r=await privilege.ExecuteAsync(verb+":"+row.Name,Queue,token).ConfigureAwait(false);message=RecordResult(r)??"Verification Inconclusive — no state result returned.";}
                }catch(OperationCanceledException){operationVerdict="Cancelled";message="Cancelled — no further service action requested.";}catch(Exception e){operationVerdict=ChangeVerdict.Failed.ToString();message="Failed — "+e.Message;}
                Dispatch(()=>{ServiceActionStatus=row.Name+": "+message;LoadServicesPreserveResult();});return message;
            });
        }
        private async void LoadServicesPreserveResult(){string result=ServiceActionStatus;while(serviceLoading)await Task.Delay(100);LoadServices();while(serviceLoading)await Task.Delay(100);if(!closed)ServiceActionStatus=result;}
        private async void LoadEvents(bool older)
        {
            if(eventLoading||closed)return;eventLoading=true;EventListStatus="Loading selected log…";
            string logName=EventLog,severity=EventSeverity,source=EventSourceFilter,id=EventIdFilter;string key=logName+"|"+severity+"|"+source+"|"+id+"|"+EventDays;
            try{int days=Integer(EventDays,1,30,"Days"),maximum=Integer(EventMaximum,1,500,"Records per page");string query=ServiceEventBrowser.EventQuery(severity,days,id,source,older&&key==eventQueryKey?nextEventId:0);
                var rows=await BoundedWork.Run("Helpdesk event page",()=>Task.FromResult(ServiceEventBrowser.Events(logName,query,maximum,CancellationToken.None)),30,CancellationToken.None);if(closed)return;
                SelectedEvent=null;EventRows.Clear();foreach(var row in rows)EventRows.Add(row);eventQueryKey=key;nextEventId=rows.Count==maximum?rows.Min(x=>x.RecordId):0;
                EventListStatus=rows.Count+" events in this page"+(nextEventId>0?" · Older events available":" · End of matching results")+". Select a row for the full message.";
            }catch(Exception e){EventRows.Clear();SelectedEvent=null;EventListStatus="Not Available — "+e.Message+". Access to Security/operational logs may require permissions; the GUI was not elevated.";nextEventId=0;}
            finally{eventLoading=false;CommandManager.InvalidateRequerySuggested();}
        }
        private async void LoadEventDetails(EventRow row)
        {
            long generation=++detailGeneration;if(row==null){EventDetails="Select an event to read its complete message.";return;}EventDetails="Loading full event message…";
            try{string detail=await BoundedWork.Run("Event detail "+row.Log+" "+row.RecordId,()=>Task.FromResult(ServiceEventBrowser.Details(row)),10,CancellationToken.None);if(!closed&&generation==detailGeneration)EventDetails=detail;}
            catch(Exception e){if(!closed&&generation==detailGeneration)EventDetails="Not Available — "+e.Message;}
        }
    }
}
