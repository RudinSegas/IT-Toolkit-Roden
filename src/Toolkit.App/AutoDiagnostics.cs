using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Toolkit.Core;
using Toolkit.Windows;
using Toolkit.Software;
namespace Toolkit.App
{
    public sealed class DiagnosticGroup
    {public string Title{get;set;}public System.Collections.Generic.List<DiagnosticRecord> Items{get;set;}}
    public sealed partial class MainViewModel
    {
        public System.Collections.Generic.IEnumerable<DiagnosticGroup> DiagnosticGroups{get{
            if(currentDiagnostic==null)return new DiagnosticGroup[0];
            var rows=currentDiagnostic.Records.Where(x=>!String.IsNullOrEmpty(x.Value));
            if(DiagnosticSection=="Storage Health")return rows.GroupBy(x=>x.Name.Split(new[]{" · "},StringSplitOptions.None)[0]).Select(g=>new DiagnosticGroup{Title=g.Key,Items=g.Select(x=>new DiagnosticRecord{Name=x.Name.StartsWith(g.Key+" · ")?x.Name.Substring(g.Key.Length+3):"Volume details",Value=x.Value,State=DiagnosticMeaning.State(x.State)==""&&x.State!="Healthy"?"":x.State}).ToList()}).ToArray();
            return new[]{new DiagnosticGroup{Title=DiagnosticSection,Items=rows.Take(100).ToList()}};
        }}
        public bool IsCardDiagnostic{get{return !IsDeviceDiagnostic&&!IsSoftwareDiagnostic;}}
        private CancellationTokenSource diagnosticCancel;
        private long diagnosticGeneration;
        private bool diagnosticLoading;
        public bool DiagnosticLoading{get{return diagnosticLoading;}private set{diagnosticLoading=value;Changed("DiagnosticLoading");Changed("DiagnosticStatus");}}
        private string diagnosticError="";
        public bool DiagnosticSearchVisible{get{return IsSoftwareDiagnostic||IsDeviceDiagnostic;}}
        public string DiagnosticSummary{get{return DiagnosticLoading?"Collecting current information…":!String.IsNullOrEmpty(diagnosticError)?diagnosticError:currentDiagnostic==null?"Select a category to collect its results.":currentDiagnostic.Summary??HelpdeskDiagnostics.Summary(currentDiagnostic);}}
        public string DiagnosticTechnicalText{get{return currentDiagnostic==null?"No details collected.":String.Join("\r\n\r\n",(currentDiagnostic.TechnicalRecords!=null&&currentDiagnostic.TechnicalRecords.Count>0?currentDiagnostic.TechnicalRecords:currentDiagnostic.Records).Select(x=>x.Name+": "+x.Value));}}
        private async void CollectDiagnostic(bool force)
        {
            if(closed||!automaticCollection&&!force)return;
            string section=DiagnosticSection;if(String.IsNullOrEmpty(section))return;
            long request=++diagnosticGeneration;
            if(diagnosticCancel!=null)diagnosticCancel.Cancel();
            if(!force&&currentDiagnostic!=null&&CategoryCachePolicy.Fresh(currentDiagnostic.Timestamp,DateTimeOffset.Now))
            {DiagnosticLoading=false;diagnosticError="";RefreshDiagnosticView();return;}
            var cancel=new CancellationTokenSource();diagnosticCancel=cancel;var token=cancel.Token;string adapter=SelectedAdapter;bool problems=ProblemDevicesOnly;
            DiagnosticLoading=true;diagnosticError="";Changed("DiagnosticSummary");
            try{
                await Task.Delay(200,token);
                DiagnosticSnapshot result;
                if(diagnosticProvider!=null)result=await diagnosticProvider(section,token);
                else if(section=="Installed Software"){
                    var inventory=await BoundedWork.Run("Helpdesk software inventory",()=>Task.FromResult(InstalledInventory.Read()),30,token);
                    result=new DiagnosticSnapshot{Section=section,Summary=inventory.Items.Count+" installed application records"+(inventory.Complete?".":" — some registry locations unavailable.")};
                    foreach(var row in inventory.Items)result.Add(row.Name,row.Version+" · "+row.Publisher+" · "+row.Scope);
                    foreach(var error in inventory.Errors)result.Add("Inventory unavailable",error,"Unknown");
                    if(CategoryCachePolicy.Accept(request,diagnosticGeneration,section,DiagnosticSection))allInstalled=inventory.Items;
                }else result=await BoundedWork.Run("Helpdesk category "+section,()=>new HelpdeskDiagnostics().CollectAsync(section,adapter,problems,token),60,token);
                if(closed||!CategoryCachePolicy.Accept(request,diagnosticGeneration,section,DiagnosticSection))return;
                Publish(result);log.Write("DiagnosticCollection",section,"Completed","Category result published without an Activity Output dump.");
            }catch(OperationCanceledException){}catch(Exception e){if(request==diagnosticGeneration&&!closed){diagnosticError="Not Available — "+e.Message+". You can retry with Refresh.";log.Write("DiagnosticCollection",section,"Failed",e.Message);}}
            finally{if(request==diagnosticGeneration&&!closed){DiagnosticLoading=false;Changed("DiagnosticSummary");Changed("DiagnosticStatus");}cancel.Dispose();if(diagnosticCancel==cancel)diagnosticCancel=null;}
        }
    }
}
