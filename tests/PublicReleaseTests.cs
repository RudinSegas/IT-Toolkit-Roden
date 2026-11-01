using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Toolkit.Core;
using Toolkit.Software;
namespace Toolkit.Tests
{
    public static partial class Suite
    {
        private static InstalledApplication AppFixture()
        {return new InstalledApplication{Name="Fixture App",Publisher="Fixture Vendor",Version="1.2",Scope="machine",Source="{12345678-1234-1234-1234-123456789ABC}",RegistryView=64,HasUninstallCommand=true};}
        private static InstallerPlan NotepadFixture()
        {return new InstallerPlan{Id="Notepad++.Notepad++",Extension=".msi",Publisher="NOTEPAD++",Version="v8.9.8.1",Hash=new string('a',64),Url="https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v8.9.8.1/npp.8.9.8.1.Installer.x64.msi",Arguments=new[]{"/qn","/norestart","REBOOT=ReallySuppress"}};}
        private static void RunPublicCore()
        {
            Check(Release.Version=="1.0.0"&&Release.Title=="IT Toolkit by Roden Segas","public version/product constants");
            var assembly=Assembly.GetExecutingAssembly();Check(assembly.GetName().Version.ToString()=="1.0.0.0","assembly version");
            Check(assembly.GetCustomAttributes(typeof(AssemblyProductAttribute),false).Cast<AssemblyProductAttribute>().Single().Product==Release.Title,"product metadata");
            foreach(string asset in new[]{"Toolkit.Icon","Toolkit.Logo"})using(var s=assembly.GetManifestResourceStream(asset))Check(s!=null&&s.Length>100,"embedded branding asset "+asset);
            var app=AppFixture();Check(app.CanUninstall,"valid selected registry application eligible");
            app.Source="x\\other";Check(!UninstallPolicy.ValidIdentity(app),"registry traversal rejected");app=AppFixture();app.RegistryView=0;Check(!UninstallPolicy.ValidIdentity(app),"ambiguous registry view rejected");
            foreach(string name in new[]{"Microsoft Edge","Microsoft .NET Runtime","Microsoft Visual C++ Redistributable","Windows Driver Kit","Cortex XDR","IT Toolkit by Roden Segas"})Check(UninstallPolicy.Protection(name,"vendor",false,false,"","")!="","protected component "+name);
            Check(UninstallPolicy.Protection("Fixture","Vendor",true,false,"","")!=""&&UninstallPolicy.Protection("Fixture","Vendor",false,true,"","")!=""&&UninstallPolicy.Protection("Fixture","Vendor",false,false,"Update","")!="","ARP system/no-remove/update protections");
            Check(UninstallPolicy.Protection("Notepad++","Notepad++ Team",false,false,"","")=="","ordinary application not misclassified");
            var reg=new UninstallRegistration{Application=AppFixture(),WindowsInstaller=true,Command="MsiExec.exe /I{12345678-1234-1234-1234-123456789ABC}"};
            var msi=UninstallPolicy.Create(reg,@"C:\Windows\System32");Check(msi.Msi&&msi.Machine&&msi.Arguments.StartsWith("/x ")&&msi.Arguments.Contains("/norestart")&&!msi.Arguments.Contains("/qn"),"registered MSI normalized to interactive product removal without reboot");
            Check(UninstallPolicy.StartInfo(msi,false).Verb=="runas"&&String.IsNullOrEmpty(UninstallPolicy.StartInfo(msi,true).Verb),"machine uninstall elevates only for non-admin token");
            msi.Machine=false;Check(String.IsNullOrEmpty(UninstallPolicy.StartInfo(msi,false).Verb)&&UninstallPolicy.StartInfo(msi,false).UseShellExecute,"current-user uninstall does not force elevation");
            string lockFile=Path.Combine(Path.GetTempPath(),"Toolkit-uninstall-lock-"+Guid.NewGuid().ToString("N"));
            try {using(var guard=new FileStream(lockFile,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))Throws(()=>{using(var duplicate=new FileStream(lockFile,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}},"exclusive uninstall gate refuses duplicate handle");}finally{File.Delete(lockFile);}
            reg.Command=reg.Command.Replace("12345678","87654321");Throws(()=>UninstallPolicy.Create(reg,@"C:\Windows\System32"),"MSI different product rejected");
            reg.Command="MsiExec.exe /X{12345678-1234-1234-1234-123456789ABC} /quiet";Throws(()=>UninstallPolicy.Create(reg,@"C:\Windows\System32"),"ambiguous MSI flags rejected");
            reg.WindowsInstaller=false;reg.Command="\"C:\\Program Files\\Fixture\\uninstall.exe\" --uninstall";var exe=UninstallPolicy.Create(reg,@"C:\Windows\System32");Check(!exe.Msi&&exe.Arguments=="--uninstall","EXE registered arguments preserved without invented silent flags");
            foreach(string bad in new[]{"cmd.exe /c evil","\"C:\\Windows\\System32\\rundll32.exe\" x","C:\\Program Files\\Fixture\\uninstall.exe", "\\\\host\\share\\u.exe","%TEMP%\\u.exe","\"C:\\Fixture\\u.exe\" /restart","\"C:\\Fixture\\u.exe\"\r\nmalformed"}){reg.Command=bad;Throws(()=>UninstallPolicy.Create(reg,@"C:\Windows\System32"),"unsafe registered command rejected");}
            reg.Application.RemovalRestriction="Protected";reg.Command="\"C:\\Fixture\\u.exe\"";Throws(()=>UninstallPolicy.Create(reg,@"C:\Windows\System32"),"protected app cannot produce a plan");
            Check(UninstallPolicy.Absence(true,false).StartsWith("Already Absent")&&UninstallPolicy.Absence(false,false).StartsWith("Verification Inconclusive")&&UninstallPolicy.Absence(true,true).StartsWith("Verification Inconclusive"),"precheck requires independently established absence");
            Check(UninstallPolicy.Verdict(0,true,false)=="Successfully Removed","fresh absence required for removal success");Check(UninstallPolicy.Verdict(0,true,true).StartsWith("Verification Inconclusive"),"exit zero with app present is not success");Check(UninstallPolicy.Verdict(0,false,false)=="Verification Inconclusive","partial inventory cannot prove absence");Check(UninstallPolicy.Verdict(1605,true,false)=="Successfully Removed","MSI already absent independently verified after launch");Check(UninstallPolicy.Verdict(3010,true,true)=="Restart Required"&&UninstallPolicy.Verdict(5,true,false)=="Failed","restart/error retained despite registration evidence");Check(UninstallPolicy.Verdict(1602,true,true).Contains("cancelled"),"vendor cancel explicit");
            Check(!Operations.IsElevated("Uninstall:C:\\app.exe")&&!Operations.IsElevated("Uninstall:cmd.exe /c x"),"uninstall cannot extend privileged helper into arbitrary execution");
            var npp=NotepadFixture();NotepadInstaller.Validate(npp);Check(true,"Notepad++ official MSI profile/hash/version");
            npp.Hash=null;Throws(()=>NotepadInstaller.Validate(npp),"Notepad++ digest required");npp=NotepadFixture();npp.Url=npp.Url.Replace("github.com","evil.invalid");Throws(()=>NotepadInstaller.Validate(npp),"Notepad++ origin rejected");npp=NotepadFixture();npp.Version="v8.9.9";Throws(()=>NotepadInstaller.Validate(npp),"Notepad++ mismatched version rejected");npp=NotepadFixture();npp.AllowUnsignedWithDigest=true;Throws(()=>NotepadInstaller.Validate(npp),"Notepad++ cannot use unsigned exception");
            Check(Catalog.InstallArguments("Notepad++.Notepad++",false,"machine").Contains("wix"),"WinGet Notepad++ selects official MSI installer family");
        }
        private static void RunPublicWindowsMetadata()
        {
            string json="{\"tag_name\":\"v8.9.8.1\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"npp.8.9.8.1.Installer.x64.exe\",\"browser_download_url\":\"https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v8.9.8.1/npp.8.9.8.1.Installer.x64.exe\"},{\"name\":\"npp.8.9.8.1.Installer.x64.msi\",\"browser_download_url\":\"https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v8.9.8.1/npp.8.9.8.1.Installer.x64.msi\",\"digest\":\"sha256:"+new string('a',64)+"\"}]}";
            var plan=NotepadInstaller.Parse(json);Check(plan.Extension==".msi"&&plan.Publisher=="NOTEPAD++","actual JSON parser selects x64 MSI over EXE");
            Throws(()=>NotepadInstaller.Parse(json.Replace("sha256:","sha512:")),"actual metadata parser fails closed without SHA256");
        }
    }
}
