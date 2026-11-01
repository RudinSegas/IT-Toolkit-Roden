using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Toolkit.Privilege;
namespace Toolkit.App
{
    public sealed partial class MainViewModel
    {
        public ICommand CreateShortcutCommand { get; private set; }
        private void InitializeShortcut()
        {
            CreateShortcutCommand=new Command(p=>{
                object shell=null,shortcut=null;
                try{
                    string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    if(String.IsNullOrWhiteSpace(desktop)||!Directory.Exists(desktop))throw new IOException("Current-user desktop is unavailable.");
                    string target=Path.Combine(desktop,"IT Toolkit by Roden Segas.lnk");
                    if(File.Exists(target)&&!Confirm("Replace the existing IT Toolkit by Roden Segas desktop shortcut?"))return;
                    var type=Type.GetTypeFromProgID("WScript.Shell",true);shell=Activator.CreateInstance(type);
                    shortcut=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{target});var link=shortcut.GetType();
                    foreach(var pair in new[]{new[]{"TargetPath",NativeSecurity.ImagePath},new[]{"WorkingDirectory",Path.GetDirectoryName(NativeSecurity.ImagePath)},new[]{"Description","IT Toolkit by Roden Segas"},new[]{"IconLocation",NativeSecurity.ImagePath+",0"}})link.InvokeMember(pair[0],BindingFlags.SetProperty,null,shortcut,new object[]{pair[1]});
                    link.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);Status="Current-user desktop shortcut created. Keep the portable folder at its current location.";
                }catch(Exception e){Status="Could not create shortcut: "+e.Message+". You can create one manually in Windows Explorer.";}
                finally{if(shortcut!=null&&Marshal.IsComObject(shortcut))Marshal.FinalReleaseComObject(shortcut);if(shell!=null&&Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);}
            },()=>!Busy);
        }
    }
}
