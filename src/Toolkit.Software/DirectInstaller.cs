using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Toolkit.Core;
using Toolkit.Privilege;

namespace Toolkit.Software
{
    public sealed class InstallerPlan
    {
        public string Id, Url, Version, Hash, Extension, Publisher;
        public bool AllowUnsignedWithDigest;
        public string[] Arguments;
    }
    public static class DirectPolicy
    {
        public static string[] Hosts(string id)
        {
            if(id=="VendorUtility.Lenovo")return new[]{"support.lenovo.com","download.lenovo.com"};
            if(id=="VendorUtility.DellClassic")return new[]{"www.dell.com","dl.dell.com","downloads.dell.com"};
            Catalog.Find(id);
            if(id=="Google.Chrome")return new[]{"dl.google.com","dl.googleusercontent.com"};
            if(id=="Mozilla.Firefox")return new[]{"download.mozilla.org","download-installer.cdn.mozilla.net","archive.mozilla.org"};
            if(id=="VideoLAN.VLC")return new[]{"download.videolan.org","get.videolan.org"};
            if(id=="Adobe.Acrobat.Reader.64-bit")return new[]{"get.adobe.com","ardownload2.adobe.com","ardownload3.adobe.com"};
            if(id=="Microsoft.VisualStudioCode")return new[]{"update.code.visualstudio.com","vscode.download.prss.microsoft.com"};
            if(id.StartsWith("Microsoft.OpenJDK."))return new[]{"aka.ms","download.visualstudio.microsoft.com"};
            return new[]{"api.github.com","github.com","release-assets.githubusercontent.com","objects.githubusercontent.com","api.adoptium.net"};
        }
        public static bool Allowed(string id, Uri uri) { return uri!=null && uri.IsAbsoluteUri && uri.Scheme=="https" && uri.Port==443 && String.IsNullOrEmpty(uri.UserInfo) && Hosts(id).Contains(uri.DnsSafeHost,StringComparer.OrdinalIgnoreCase); }
        public static void CheckHash(string expected,string actual) { if(!Regex.IsMatch(expected??"", "\\A[0-9a-fA-F]{64}\\z") || !String.Equals(expected,actual,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Installer SHA-256 does not match vendor metadata. No provider fallback was performed."); }
    }
    public sealed class DirectInstaller
    {
        private static readonly HttpClient client = CreateClient();
        private static HttpClient CreateClient() { var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(15)}; client.DefaultRequestHeaders.UserAgent.ParseAdd("IT-Toolkit-Roden/1.0.0"); return client; }
        private Action<OutputChunk> sink;
        private string stage="Resolve";
        private Task<HttpResponseMessage> Get(string id,string url,CancellationToken token)
        {return DownloadTransport.Get(client,id,url,stage,text=>Emit(sink,text),token);}
        private async Task<string> Metadata(string id,string url,CancellationToken token)
        { using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){deadline.CancelAfter(TimeSpan.FromSeconds(30)); token=deadline.Token; using(var response=await Get(id,url,token).ConfigureAwait(false))using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var data=new MemoryStream()){var bytes=new byte[8192];int n;while((n=await input.ReadAsync(bytes,0,bytes.Length,token).ConfigureAwait(false))>0){if(data.Length+n>4*1024*1024)throw new IOException("Metadata exceeds limit.");data.Write(bytes,0,n);}return Encoding.UTF8.GetString(data.ToArray());} } }
        public static InstallerPlan ParseRelease(string id,string json,string repository,string pattern,string publisher,string[] args,bool allowUnsigned=false)
        {
            var root=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<Dictionary<string,object>>(json);
            if(root.ContainsKey("draft") && Convert.ToBoolean(root["draft"]) || root.ContainsKey("prerelease") && Convert.ToBoolean(root["prerelease"]))throw new InvalidDataException("Preview releases are not supported.");
            foreach(var item in (IEnumerable)root["assets"])
            {
                var asset=(Dictionary<string,object>)item;string name=Convert.ToString(asset["name"]);if(!Regex.IsMatch(name,pattern,RegexOptions.IgnoreCase))continue;
                string url=Convert.ToString(asset["browser_download_url"]);if(!url.StartsWith("https://github.com/"+repository+"/releases/download/",StringComparison.Ordinal))throw new InvalidDataException("Release asset repository mismatch.");
                string digest=asset.ContainsKey("digest")?Convert.ToString(asset["digest"]):null;
                return new InstallerPlan{Id=id,Url=url,Version=Convert.ToString(root["tag_name"]),Hash=digest!=null && digest.StartsWith("sha256:")?digest.Substring(7):null,Extension=Path.GetExtension(name),Publisher=publisher,Arguments=args,AllowUnsignedWithDigest=allowUnsigned};
            }throw new InvalidDataException("Official release has no supported x64 installer. Distribution format may have changed.");
        }
        public async Task<InstallerPlan> ResolveAsync(string id,CancellationToken token)
        {
            Catalog.Find(id); var p=new InstallerPlan{Id=id,Version="Unknown (stable vendor endpoint)",Extension=".exe"};
            switch(id)
            {
                case "Google.Chrome":p.Url="https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi";p.Extension=".msi";p.Publisher="Google LLC";break;
                case "Mozilla.Firefox":p.Url="https://download.mozilla.org/?product=firefox-latest-ssl&os=win64&lang=en-US";p.Publisher="Mozilla Corporation";p.Arguments=new[]{"/S"};break;
                case "Microsoft.VisualStudioCode":p.Url="https://update.code.visualstudio.com/latest/win32-x64-user/stable";p.Publisher="Microsoft Corporation";p.Arguments=new[]{"/VERYSILENT","/NORESTART","/MERGETASKS=!runcode"};break;
                case "7zip.7zip":return ParseRelease(id,await Metadata(id,"https://api.github.com/repos/ip7z/7zip/releases/latest",token).ConfigureAwait(false),"ip7z/7zip",@"^7z[0-9]+-x64\.exe$","Igor Pavlov",new[]{"/S"},true);
                case "Notepad++.Notepad++":return NotepadInstaller.Parse(await Metadata(id,"https://api.github.com/repos/notepad-plus-plus/notepad-plus-plus/releases/latest",token).ConfigureAwait(false));
                case "Microsoft.PowerToys":return ParseRelease(id,await Metadata(id,"https://api.github.com/repos/microsoft/PowerToys/releases/latest",token).ConfigureAwait(false),"microsoft/PowerToys",@"^PowerToysSetup-[0-9.]+-x64\.exe$","Microsoft Corporation",new[]{"/quiet","/norestart"});
                case "VideoLAN.VLC":
                    string page=await Metadata(id,"https://download.videolan.org/pub/videolan/vlc/last/win64/",token).ConfigureAwait(false);
                    var match=Regex.Match(page,@"href=""(vlc-[0-9.]+-win64\.exe)""",RegexOptions.IgnoreCase);
                    if(!match.Success)throw new InvalidDataException("Official VLC directory did not expose a supported installer.");
                    p.Url="https://download.videolan.org/pub/videolan/vlc/last/win64/"+match.Groups[1].Value;p.Publisher="VideoLAN";p.Arguments=new[]{"/S"};p.Version=Regex.Match(match.Groups[1].Value,@"[0-9]+(?:\.[0-9]+)+").Value;break;
                case "Adobe.Acrobat.Reader.64-bit":
                    string adobe=await Metadata(id,"https://get.adobe.com/reader/enterprise/",token).ConfigureAwait(false);
                    var link=Regex.Match(adobe,@"https://ardownload[23]\.adobe\.com/[^""'<>\s]*AcroRdrDCx64[0-9]+_en_US\.exe",RegexOptions.IgnoreCase);
                    if(!link.Success)throw new InvalidDataException("Adobe did not expose a supported standalone Reader installer in its official distribution page. Use the official vendor page/authorized distribution agreement or explicitly select WinGet. No guessed URL or unverified installer was used.");
                    p.Url=link.Value;p.Publisher="Adobe Inc.|Adobe Systems Incorporated";p.Arguments=new[]{"/sAll","/rs","/rps","/msi","EULA_ACCEPT=YES","REBOOT=ReallySuppress"};break;
                default:
                    if(id.StartsWith("Microsoft.OpenJDK.")){string major=id.Substring("Microsoft.OpenJDK.".Length);p.Url="https://aka.ms/download-jdk/microsoft-jdk-"+major+"-windows-x64.msi";p.Extension=".msi";p.Publisher="Microsoft Corporation";}
                    else if(id.StartsWith("EclipseAdoptium.Temurin.")){
                        string major=id.Split('.')[2];string json=await Metadata(id,"https://api.adoptium.net/v3/assets/latest/"+major+"/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse",token).ConfigureAwait(false);
                        var list=new JavaScriptSerializer().Deserialize<object[]>(json);if(list.Length==0)throw new InvalidDataException("Temurin metadata is empty.");var asset=(Dictionary<string,object>)list[0];var binary=(Dictionary<string,object>)asset["binary"];var installer=(Dictionary<string,object>)binary["installer"];
                        p.Url=Convert.ToString(installer["link"]);if(!p.Url.StartsWith("https://github.com/adoptium/temurin"+major+"-binaries/releases/download/",StringComparison.Ordinal))throw new InvalidDataException("Unexpected Temurin repository.");p.Hash=Convert.ToString(installer["checksum"]);p.Extension=".msi";p.Publisher="Eclipse Foundation, Inc.|Eclipse Foundation AISBL|Eclipse.org Foundation, Inc.";p.Version=Convert.ToString(asset["release_name"]);
                    }else throw new ArgumentException("Unsupported direct installer.");break;
            }
            if(p.Extension==".msi")p.Arguments=new[]{"/qn","/norestart","REBOOT=ReallySuppress"};return p;
        }
        public async Task<string> CheckWithoutElevationAsync(string id,bool update,CancellationToken token)
        {
            var d=Catalog.Find(id);var inventory=await Task.Run(()=>InstalledInventory.Read()).ConfigureAwait(false);
            if(!inventory.Complete)return null;
            var installed=inventory.Items.Where(x=>Matches(x,d)).ToArray();if(installed.Length==0)return null;
            if(!update)return d.Name+": Already Installed — Update Status Unknown; version "+String.Join(", ",installed.Select(x=>x.Version))+". No elevation or reinstall required.";
            InstallerPlan plan=null;try{plan=await ResolveAsync(id,token).ConfigureAwait(false);}catch(OperationCanceledException){throw;}catch{}
            return plan!=null&&KnownVersion(plan.Version)&&installed.All(x=>ExactVersion(plan.Version,x.Version))?d.Name+": Up to Date — official version "+plan.Version+" verified; no elevation or reinstall.":null;
        }
        public async Task<string> InstallAsync(string id,Action<OutputChunk> output,CancellationToken token,bool update=false)
        {return (await InstallResultAsync(id,output,token,update).ConfigureAwait(false)).Summary;}
        public async Task<ProcessResult> InstallResultAsync(string id,Action<OutputChunk> output,CancellationToken token,bool update=false)
        {
            Catalog.Find(id); sink=output;
            string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IT-Admin-Toolkit","InstallLocks"); Directory.CreateDirectory(root);
            using(var directory=new DirectoryLease(root))
            {
                string path=Path.Combine(root,Regex.Replace(id,"[^A-Za-z0-9.-]","_")+".lock");
                if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Installer lock is a reparse point.");
                using(var gate=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                    return await InstallCoreAsync(id,output,token,update).ConfigureAwait(false);
            }
        }
        private async Task<ProcessResult> InstallCoreAsync(string id,Action<OutputChunk> output,CancellationToken token,bool update)
        {
            stage="Detect";Emit(output,"Detect: reading installed application evidence.");
            var definition=Catalog.Find(id);var initial=InstalledInventory.Read();var before=initial.Items.Where(x=>Matches(x,definition)).ToArray();
            if(!initial.Complete)throw new IOException("Detect Failed: registry inventory incomplete; refusing a potentially redundant installation.");
            if(before.Length>0&&!update){string existing=definition.Name+": Already Installed — Update Status Unknown; version "+String.Join(", ",before.Select(x=>x.Version));Emit(output,existing);return StateWorkflow.Result(ChangeVerdict.AlreadyConfigured,existing);}
            stage="Resolve";Emit(output,"Resolving Download");
            InstallerPlan p;
            try{p=await ResolveAsync(id,token).ConfigureAwait(false);}catch(OperationCanceledException e){if(token.IsCancellationRequested)throw;throw new IOException("Download Failed during Resolve: metadata deadline exceeded.",e);}catch(Exception e){throw new IOException("Download Failed during Resolve: "+e.Message,e);}
            Emit(output,"Available version: "+p.Version+"; official source: "+p.Url);
            if(before.Length>0 && KnownVersion(p.Version) && before.All(x=>ExactVersion(p.Version,x.Version)))
            {string current=definition.Name+": Up to Date — verified official version "+p.Version+"; no installer executed.";Emit(output,current);return StateWorkflow.Result(ChangeVerdict.AlreadyConfigured,current);}
            if(before.Length>0&&!KnownVersion(p.Version))Emit(output,"Installed — Update Status Unknown. Explicit update requested; vendor stable endpoint will be used. No Up to Date claim.");
            bool elevated;using(var identity=WindowsIdentity.GetCurrent())elevated=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            string root=elevated?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"IT-Admin-Toolkit-Servicing"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IT-Admin-Toolkit","Installers");
            Directory.CreateDirectory(root);string directory=Path.Combine(root,Guid.NewGuid().ToString("N"));
            var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
            if(elevated){var admins=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null);acl.SetOwner(admins);foreach(var sid in new[]{admins,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));}
            else using(var identity=WindowsIdentity.GetCurrent())acl.AddAccessRule(new FileSystemAccessRule(identity.User,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            new DirectoryInfo(directory).Create(acl);
            string file=Path.Combine(directory,"installer"+p.Extension);bool finished=false; int? nativeCode=null;
            try
            {
                using(var lease=new DirectoryLease(directory))
                {
                    stage="Download";Emit(output,"Downloading");
                    using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token))
                    {timeout.CancelAfter(TimeSpan.FromMinutes(15));using(var response=await Get(id,p.Url,timeout.Token).ConfigureAwait(false))using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var dest=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){
                        long total=response.Content.Headers.ContentLength??0,received=0;var bytes=new byte[65536];int n;var clock=Stopwatch.StartNew();long tick=0;
                        while((n=await input.ReadAsync(bytes,0,bytes.Length,timeout.Token).ConfigureAwait(false))>0){received+=n;if(received>1024L*1024*1024)throw new IOException("Installer exceeds 1 GiB safety limit.");await dest.WriteAsync(bytes,0,n,timeout.Token).ConfigureAwait(false);if(clock.ElapsedMilliseconds-tick>250){tick=clock.ElapsedMilliseconds;if(output!=null)output(new OutputChunk{Channel="status",Text="\rDownload "+(received/1048576.0).ToString("N1")+" MB"+(total>0?" / "+(total/1048576.0).ToString("N1")+" MB":""),MeasuredPercent=total>0?(double?)(received*100.0/total):null});}}
                        if(total>0&&received!=total)throw new IOException("Incomplete download.");if(received<1024)throw new InvalidDataException("Installer content is too small.");
                    }}
                    stage="Verify";Emit(output,"Verifying Installer");InternetZone.Mark(file);
                    using(var held=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
                    {
                        string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(held)).Replace("-","").ToLowerInvariant();if(p.Hash!=null)DirectPolicy.CheckHash(p.Hash,hash);
                        Signature.VerifyPublisher(file,p.Publisher,p.AllowUnsignedWithDigest && p.Hash!=null);Emit(output,"Security verified; SHA-256 "+hash+". Starting silent installer. Progress is indeterminate during installation.");
                        token.ThrowIfCancellationRequested();string executable=file;var args=p.Arguments;
                        if(p.Extension==".msi"){executable=Path.Combine(Environment.SystemDirectory,"msiexec.exe");args=new[]{"/i",file}.Concat(args).ToArray();}
                        stage="Install";Emit(output,"Installing");
                        var result=await new ProcessEngine().RunAsync(executable,args,Encoding.UTF8,output,token,false,0,true).ConfigureAwait(false);finished=true;nativeCode=result.ExitCode;
                        stage="Validate";Emit(output,"Installation launcher completed; native exit "+result.ExitCode+". Verifying Installed Application");
                        if(id=="Microsoft.VisualStudioCode") { string code=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Microsoft VS Code","Code.exe"); if(!File.Exists(code))throw new IOException("Post-install VS Code executable was not found; success cannot be verified."); Signature.VerifyMicrosoft(code); }
                        var inventory=InstalledInventory.Read();var after=inventory.Items.Where(x=>Matches(x,definition)).ToArray();string version=after.Select(x=>x.Version).Distinct().Count()==1?after[0].Version:null;
                        string prior=before.Select(x=>x.Version).FirstOrDefault();bool versionMatched = VersionMatches(p.Version,version); string verdict=InstallationVerification.Classify(result.ExitCode,prior,version,inventory.Complete&&after.Length>0&&versionMatched,before.Length>0);
                        string summary=definition.Name+": "+verdict+"\r\nExit: "+result.ExitCode+"; reboot required: "+result.RebootRequired+"\r\nInstalled version: "+(version??"Unknown")+"; registry name/publisher/scope evidence, not license validation.";var final=result.ExitCode!=0&&!result.RebootRequired?ChangeVerdict.Failed:result.RebootRequired?ChangeVerdict.RestartRequired:inventory.Complete&&after.Length>0&&versionMatched&&(!update||prior!=version)?ChangeVerdict.SuccessfullyChanged:ChangeVerdict.VerificationInconclusive;
                        summary=definition.Name+": "+final+"\r\n"+summary;Emit(output,summary);return StateWorkflow.Result(final,summary,result.ExitCode);
                    }
                }
            }
            catch(OperationCanceledException){if(token.IsCancellationRequested)throw;return StateWorkflow.Result(ChangeVerdict.Failed,definition.Name+": "+stage+" Failed — network deadline exceeded; installer exit: "+(nativeCode.HasValue?nativeCode.Value.ToString():"not executed"),nativeCode);}
            catch(Exception e){string label=stage=="Download"||stage=="Resolve"?"Download Failed":stage=="Verify"?"Verification Failed":stage=="Install"?"Installation Failed":"Verification Inconclusive";string failure=definition.Name+": "+label+" during "+stage+": "+e.GetType().Name+": "+e.Message+"; installer exit: "+(nativeCode.HasValue?nativeCode.Value.ToString():"not available / not completed");Emit(output,failure);return StateWorkflow.Result(stage=="Validate"&&(!nativeCode.HasValue||nativeCode==0||nativeCode==3010||nativeCode==1641)?ChangeVerdict.VerificationInconclusive:ChangeVerdict.Failed,failure,nativeCode);}
            finally{Emit(output,"Cleanup");try{if(File.Exists(file))File.Delete(file);Directory.Delete(directory,false);Emit(output,"Temporary installer cleaned up.");}catch{Emit(output,"Cleanup deferred: installer file is still in use. No active process was terminated. "+(finished?"Installer launcher exited.":"Review operation logs."));}}
        }
        public static bool KnownVersion(string value){return !String.IsNullOrWhiteSpace(value)&&!value.StartsWith("Unknown")&&Regex.IsMatch(value,@"[0-9]+\.[0-9]+");}
        public static bool ExactVersion(string available,string installed)
        {
            if(!KnownVersion(available)||!KnownVersion(installed))return false;
            Func<string,string[]> parts=v=>Regex.Matches(v,@"[0-9]+").Cast<Match>().Select(m=>m.Value.TrimStart('0').PadLeft(1,'0')).ToArray();
            var a=parts(available).ToList();var b=parts(installed).ToList();while(a.Count>1&&a.Last()=="0")a.RemoveAt(a.Count-1);while(b.Count>1&&b.Last()=="0")b.RemoveAt(b.Count-1);return a.SequenceEqual(b);
        }
        public static bool VersionMatches(string available,string installed)
        {
            if(String.IsNullOrWhiteSpace(installed)||installed=="Unknown")return false;
            if(String.IsNullOrWhiteSpace(available)||available.StartsWith("Unknown"))return true;
            return ExactVersion(available,installed);
        }
        private static bool Matches(InstalledApplication x,PackageDefinition d){return Regex.IsMatch(x.Name,d.NamePattern,RegexOptions.IgnoreCase)&&Regex.IsMatch(x.Publisher,d.PublisherPattern,RegexOptions.IgnoreCase)&&(d.Id=="Microsoft.VisualStudioCode"?x.Scope=="user":x.Scope=="machine");}
        private static void Emit(Action<OutputChunk> output,string text){if(output==null)return; text="\r\n"+text+"\r\n";for(int i=0;i<text.Length;i+=1024)output(new OutputChunk{Channel="status",Text=text.Substring(i,Math.Min(1024,text.Length-i))});}
    }
}
