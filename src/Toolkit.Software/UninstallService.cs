using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Toolkit.Core;
using Toolkit.Privilege;
namespace Toolkit.Software
{
    public sealed class UninstallRegistration
    {
        public InstalledApplication Application;
        public string Command;
        public bool WindowsInstaller;
    }
    public sealed class UninstallPlan
    {
        public string Executable,Arguments;
        public bool Msi,Machine;
    }
    public static class UninstallPolicy
    {
        public static bool ValidIdentity(InstalledApplication app)
        {
            return app!=null&&(app.Scope=="machine"||app.Scope=="user")&&(app.RegistryView==32||app.RegistryView==64)&&!String.IsNullOrWhiteSpace(app.Source)&&app.Source.Length<=255&&app.Source.IndexOfAny(new[]{'\\','/','\0','\r','\n'})<0;
        }
        public static string Protection(string name,string publisher,bool system,bool noRemove,string releaseType,string parent)
        {
            if(system||noRemove||!String.IsNullOrWhiteSpace(parent)||!String.IsNullOrWhiteSpace(releaseType))return "System component, protected child/update, or removal disabled by its publisher.";
            if(Regex.IsMatch((name??"")+" "+(publisher??""),@"(?i)Windows\s+(?:SDK|Kits|Software|Driver|Update|Setup|Security)|Microsoft Edge|WebView|\.NET|Visual C\+\+|Redistributable|Defender|Endpoint|CrowdStrike|Cortex|Sentinel|Sophos|Symantec|McAfee|Trellix|BitLocker|Antivirus|Security Agent|IT Toolkit|IT Admin Toolkit"))return "Windows/runtime/security or toolkit component: use its authorized management mechanism.";
            if((publisher??"").StartsWith("Microsoft",StringComparison.OrdinalIgnoreCase)&&!Regex.IsMatch(name??"",@"(?i)^Microsoft (?:Visual Studio Code|PowerToys|Build of OpenJDK|OpenJDK)"))return "Microsoft component is protected by the toolkit's conservative removal policy.";
            return "";
        }
        public static UninstallPlan Create(UninstallRegistration registration,string systemDirectory)
        {
            var app=registration==null?null:registration.Application;
            if(!ValidIdentity(app))throw new InvalidDataException("The selected application has no valid registry identity. Refresh inventory.");
            if(app.IsProtected)throw new InvalidOperationException(app.RemovalRestriction);
            string command=(registration.Command??"").Trim();
            if(command.Length==0||command.Length>32767||command.IndexOfAny(new[]{'\0','\r','\n'})>=0)throw new InvalidDataException("A valid registered interactive uninstall command is unavailable.");
            string exe,args;
            if(command.StartsWith("\"",StringComparison.Ordinal)){int end=command.IndexOf('"',1);if(end<2)throw new InvalidDataException("Malformed quoted uninstall path.");exe=command.Substring(1,end-1);args=command.Substring(end+1).Trim();}
            else {int end=command.IndexOf(' ');exe=end<0?command:command.Substring(0,end);args=end<0?"":command.Substring(end+1).Trim();}
            string filename=exe.Replace('/','\\').Split('\\').Last();
            if(registration.WindowsInstaller)
            {
                Guid product;
                if(!Guid.TryParse(app.Source,out product)||!Regex.IsMatch(app.Source,@"\A\{[0-9a-fA-F-]{36}\}\z")||!String.Equals(filename,"msiexec.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The MSI registration does not identify a supported product-code command.");
                string msiPath=Path.Combine(systemDirectory,"msiexec.exe");
                if(exe.Contains("\\")&&!String.Equals(exe,msiPath,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("MSI executable must be Windows System32 msiexec.exe.");
                var match=Regex.Match(args,@"\A/[iIxX]\s*(\{[0-9a-fA-F-]{36}\})\s*\z");
                if(!match.Success||!String.Equals(match.Groups[1].Value,app.Source,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("MSI command is ambiguous or targets a different product. Use Windows Installed apps.");
                return new UninstallPlan{Executable=msiPath,Arguments="/x "+product.ToString("B")+" /norestart REBOOT=ReallySuppress",Msi=true,Machine=app.Scope=="machine"};
            }
            if(!Regex.IsMatch(exe,@"\A[A-Za-z]:\\")||exe.StartsWith(@"\\",StringComparison.Ordinal)||exe.Contains("..")||exe.Contains('%')||exe.IndexOf(':',2)>=0||!exe.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Uninstaller must be a quoted absolute local EXE path; shell commands, relative paths, variables and ambiguous unquoted paths are refused.");
            if(Regex.IsMatch(filename,@"(?i)\A(?:cmd|powershell|pwsh|wscript|cscript|mshta|rundll32|regsvr32|msiexec|explorer|schtasks|reg|shutdown|installutil|wmic)\.exe\z"))throw new InvalidDataException("Shell/script/system proxy commands are not supported uninstallers.");
            if(Regex.IsMatch(args,@"(?i)(?:^|\s)(?:/|--?)(?:reboot|restart|logoff|boot)(?:\s|=|$)|REBOOT\s*=\s*(?:Force|ReallyForce)"))throw new InvalidDataException("Registered command requests restart/logoff; use an authorized manual removal workflow.");
            return new UninstallPlan{Executable=exe,Arguments=args,Msi=false,Machine=app.Scope=="machine"};
        }
        public static ProcessStartInfo StartInfo(UninstallPlan plan,bool administrator)
        {
            var start=new ProcessStartInfo(plan.Executable,plan.Arguments){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(plan.Executable),WindowStyle=ProcessWindowStyle.Normal};
            if(plan.Machine&&!administrator)start.Verb="runas";
            return start;
        }
        public static string Absence(bool complete,bool present)
        { return complete&&!present?"Already Absent — no matching application registration remains; no uninstaller launched.":"Verification Inconclusive — selected registration disappeared but complete application absence was not established. Refresh inventory."; }
        public static string Verdict(int? code,bool complete,bool present)
        {
            if(code==3010||code==1641)return "Restart Required";
            if(code==1602||code==1223)return "Verification Inconclusive — cancelled; review installed state";
            if(!code.HasValue)return "Verification Inconclusive";
            if(code!=0&&code!=1605)return "Failed";
            if(!complete)return "Verification Inconclusive";
            if(!present)return "Successfully Removed";
            return "Verification Inconclusive — application remains registered; an interactive child uninstaller may still be running";
        }
    }
    public sealed class UninstallService
    {
        internal const string RegistryPath=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        internal static InstalledApplication Describe(RegistryKey key,string child,string scope,int view)
        {
            var app=new InstalledApplication{Name=Convert.ToString(key.GetValue("DisplayName")),Version=Convert.ToString(key.GetValue("DisplayVersion","Unknown")),Publisher=Convert.ToString(key.GetValue("Publisher","Unknown")),InstallDate=Convert.ToString(key.GetValue("InstallDate","Unknown")),Scope=scope,RegistryView=view,Architecture="Unknown ("+view+"-bit registry view)",Source=child};
            app.RemovalRestriction=UninstallPolicy.Protection(app.Name,app.Publisher,IsOne(key.GetValue("SystemComponent")),IsOne(key.GetValue("NoRemove")),Convert.ToString(key.GetValue("ReleaseType")),Convert.ToString(key.GetValue("ParentKeyName")));
            app.HasUninstallCommand=!String.IsNullOrWhiteSpace(Convert.ToString(key.GetValue("UninstallString")));
            return app;
        }
        private static bool IsOne(object value){return value!=null&&Convert.ToString(value)=="1";}
        private static UninstallRegistration Read(InstalledApplication identity)
        {
            if(!UninstallPolicy.ValidIdentity(identity))throw new InvalidDataException("Invalid selected registry identity.");
            using(var hive=RegistryKey.OpenBaseKey(identity.Scope=="machine"?RegistryHive.LocalMachine:RegistryHive.CurrentUser,identity.RegistryView==64?RegistryView.Registry64:RegistryView.Registry32))
            using(var key=hive.OpenSubKey(RegistryPath+"\\"+identity.Source))
            {
                if(key==null)return null;
                return new UninstallRegistration{Application=Describe(key,identity.Source,identity.Scope,identity.RegistryView),Command=Convert.ToString(key.GetValue("UninstallString")),WindowsInstaller=IsOne(key.GetValue("WindowsInstaller"))};
            }
        }
        private static bool Same(InstalledApplication a,InstalledApplication b){return String.Equals(a.Name,b.Name,StringComparison.Ordinal)&&String.Equals(a.Publisher,b.Publisher,StringComparison.Ordinal)&&String.Equals(a.Version,b.Version,StringComparison.Ordinal);}
        private static bool Present(InstalledApplication selected,SoftwareSnapshot snapshot)
        {return snapshot.Items.Any(a=>a.Scope==selected.Scope&&((a.Source==selected.Source&&a.RegistryView==selected.RegistryView)||(String.Equals(a.Name,selected.Name,StringComparison.OrdinalIgnoreCase)&&String.Equals(a.Publisher,selected.Publisher,StringComparison.OrdinalIgnoreCase))));}
        private static string CheckAbsent(InstalledApplication selected)
        {var snapshot=InstalledInventory.Read();return UninstallPolicy.Absence(snapshot.Complete,Present(selected,snapshot));}
        private static void CheckMachinePath(string path)
        {
            var roots=new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)};
            if(!roots.Any(r=>!String.IsNullOrWhiteSpace(r)&&path.StartsWith(r.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Machine EXE uninstallers must be under protected Program Files. Use Windows Installed apps for other locations.");
            string boundary=roots.Where(r=>!String.IsNullOrWhiteSpace(r)&&path.StartsWith(r.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)).OrderByDescending(r=>r.Length).First();
            string current=Path.GetFullPath(path);
            var trusted=new[]{new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),new SecurityIdentifier("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464")};
            var write=FileSystemRights.WriteData|FileSystemRights.AppendData|FileSystemRights.WriteAttributes|FileSystemRights.WriteExtendedAttributes|FileSystemRights.Delete|FileSystemRights.DeleteSubdirectoriesAndFiles|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership;
            while(current!=null){if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Reparse paths are refused for uninstallers.");FileSystemSecurity acl=File.Exists(current)?(FileSystemSecurity)File.GetAccessControl(current):Directory.GetAccessControl(current);
                if(!trusted.Contains((SecurityIdentifier)acl.GetOwner(typeof(SecurityIdentifier))))throw new InvalidDataException("Machine uninstaller path is not owned by administrators, SYSTEM or TrustedInstaller.");
                foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier)))if((rule.PropagationFlags&PropagationFlags.InheritOnly)==0&&rule.AccessControlType==AccessControlType.Allow&&!trusted.Contains((SecurityIdentifier)rule.IdentityReference)&&(rule.FileSystemRights&write)!=0)throw new InvalidDataException("Uninstaller path allows an untrusted identity to modify it.");
                if(String.Equals(current.TrimEnd('\\'),boundary.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))break;
                current=Path.GetDirectoryName(current.TrimEnd('\\'));if(String.IsNullOrEmpty(current))break;
            }
        }

        public async Task<string> RemoveAsync(InstalledApplication selected,Action<OutputChunk> output,CancellationToken token)
        {
            if(!UninstallPolicy.ValidIdentity(selected))throw new InvalidDataException("Refresh inventory and select an application.");
            string lockRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IT-Admin-Toolkit","UninstallLocks");Directory.CreateDirectory(lockRoot);
            using(var lease=new DirectoryLease(lockRoot))
            // One uninstall at a time per initiating user, including multiple toolkit instances.
            using(var gate=new FileStream(Path.Combine(lockRoot,"uninstall.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                StateWorkflow.Emit(output,"Check: rereading selected uninstall registration.");
                string pending;
                using(var sha=SHA256.Create())pending=Path.Combine(lockRoot,BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(selected.Scope+"|"+selected.RegistryView+"|"+selected.Source+"|"+selected.Version))).Replace("-","")+".pending");
                var before=Read(selected);if(before==null){string absent=CheckAbsent(selected);if(absent.StartsWith("Already Absent")&&File.Exists(pending))File.Delete(pending);return absent;}
                if(File.Exists(pending))return "Verification Inconclusive — a previous uninstaller's completion was not verified. Duplicate launch refused. Check Windows Installed apps and the previous vendor window; see docs/Uninstall.md before clearing the pending marker: "+pending;
                if(!Same(selected,before.Application))return "Verification Inconclusive — selected application identity/version changed; refresh and confirm again.";
                var plan=UninstallPolicy.Create(before,Environment.SystemDirectory);
                token.ThrowIfCancellationRequested();
                using(var image=new FileStream(plan.Executable,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    if(plan.Msi)Signature.VerifyMicrosoft(plan.Executable);
                    else {if(plan.Machine)CheckMachinePath(plan.Executable);Signature.VerifyTrusted(plan.Executable);}
                    // Recheck after trust validation, immediately before handing control to Windows.
                    var recheck=Read(selected);if(recheck==null)return CheckAbsent(selected);
                    if(recheck.Application.IsProtected||!Same(before.Application,recheck.Application)||before.Command!=recheck.Command||before.WindowsInstaller!=recheck.WindowsInstaller)throw new InvalidDataException("Uninstall registration changed after confirmation. No process launched.");
                    bool admin;using(var identity=WindowsIdentity.GetCurrent())admin=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                    var start=UninstallPolicy.StartInfo(plan,admin);
                    token.ThrowIfCancellationRequested();StateWorkflow.Emit(output,"Decision: selected application is present. Execute its registered interactive uninstaller; no silent switches or restart authorization added.");
                    int? exit=null;
                    File.WriteAllText(pending,"Uninstaller completion pending. Created "+DateTimeOffset.UtcNow.ToString("o"));
                    bool launched=false;
                    try {
                    try{using(var process=await Task.Run(()=>Process.Start(start)).ConfigureAwait(false)){
                        launched=true;
                        if(process==null)return "Verification Inconclusive — Windows did not return an uninstaller process handle.";
                        bool announced=false;while(!process.HasExited){if(token.IsCancellationRequested&&!announced){announced=true;StateWorkflow.Emit(output,"Cancellation is advisory after launch. Use the uninstaller's Cancel button; it will not be force-terminated.");}await Task.Delay(500).ConfigureAwait(false);}exit=process.ExitCode;
                    }}catch(Win32Exception e){if(e.NativeErrorCode==1223){File.Delete(pending);return "Verification Inconclusive — UAC/uninstaller launch cancelled; no removal was authorized by the toolkit.";}throw;}
                    StateWorkflow.Emit(output,"Post-check: native exit "+exit+"; independently refreshing installed-software inventory.");
                    SoftwareSnapshot after=null;bool present=true;
                    for(int attempt=0;attempt<10;attempt++){
                        after=InstalledInventory.Read();present=Present(selected,after);
                        if(!present&&after.Complete)break;if(exit!=0&&exit!=1605)break;await Task.Delay(1000).ConfigureAwait(false);
                    }
                    if(after!=null&&after.Complete&&!present||exit==1602||exit==1223||exit.HasValue&&exit!=0&&exit!=1605&&exit!=3010&&exit!=1641)File.Delete(pending);
                    return UninstallPolicy.Verdict(exit,after!=null&&after.Complete,present)+" — "+selected.Name+". Exit: "+exit+"; registry evidence: "+(present?"still present":"absent")+"; complete inventory: "+(after!=null&&after.Complete)+". This is registration verification, not a claim that all vendor files/data were deleted.";
                    } finally {if(!launched&&File.Exists(pending))File.Delete(pending);}
                }
            }
        }
    }
}
