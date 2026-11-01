using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Toolkit.Software;
using Toolkit.Windows;
namespace Toolkit.App
{
    public sealed partial class MainViewModel
    {
        public ICommand UninstallCommand { get; private set; }
        public bool CanUninstallSelected { get { return !Busy && SelectedInventory!=null && SelectedInventory.CanUninstall; } }
        private string uninstallStatus="";
        public string UninstallStatus { get { return uninstallStatus; } private set { uninstallStatus=value;Changed("UninstallStatus"); } }
        public string UninstallSelectionInfo { get { return SelectedInventory==null?"Select an application to inspect removal availability.":SelectedInventory.UninstallAvailability; } }
        private void InitializeUninstall()
        {
            UninstallCommand=new Command(p=>UninstallSelected(),()=>CanUninstallSelected);
        }
        private void UninstallSelected()
        {
            var selected=SelectedInventory;if(!CanUninstallSelected)return;
            if(!Confirm("Uninstall this application?\r\n\r\nName: "+selected.Name+"\r\nPublisher: "+selected.Publisher+"\r\nVersion: "+selected.Version+"\r\nScope: "+selected.Scope+"\r\n\r\nIts registered interactive uninstaller will open. Windows may request elevation for machine applications. No silent arguments or automatic restart/logoff authorization will be added. Save your work; cancel in the vendor window if needed."))return;
            UninstallStatus="Checking selected application…";
            Start("Uninstall · "+selected.Name,async token=>{
                string result;
                try { result=await new UninstallService().RemoveAsync(selected,Queue,token).ConfigureAwait(false); }
                catch(OperationCanceledException){result="Verification Inconclusive — cancelled before launch; no further action requested.";}
                catch(Exception e){result="Failed — "+e.Message;}
                operationVerdict=result.StartsWith("Successfully Removed")?"Successfully Removed":result.StartsWith("Already Absent")?"Already Absent":result.StartsWith("Restart Required")?"Restart Required":result.StartsWith("Failed")?"Failed":"Verification Inconclusive";
                var refreshed=await Task.Run(()=>InstalledInventory.Read()).ConfigureAwait(false);
                var snapshot=new DiagnosticSnapshot{Section="Installed Software",Summary=refreshed.Items.Count+" installed application records"+(refreshed.Complete?".":" — some registry locations unavailable.")};
                foreach(var app in refreshed.Items)snapshot.Add(app.Name,app.Version+" · "+app.Publisher+" · "+app.Scope);
                foreach(var error in refreshed.Errors)snapshot.Add("Inventory unavailable",error,"Unknown");
                Dispatch(()=>{allInstalled=refreshed.Items;diagnosticSnapshots[snapshot.Section]=snapshot;if(DiagnosticSection==snapshot.Section){currentDiagnostic=snapshot;RefreshDiagnosticView();}FilterRows();SelectedInventory=null;UninstallStatus=result;});
                return result;
            });
        }
    }
}
