using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Toolkit.Core;
using Toolkit.Privilege;
using Toolkit.Windows;
namespace Toolkit.Software
{
    public sealed class VendorDownloadResult{public bool Downloaded;public string Path,Message,Page;}
    public static class VendorUtilityDownload
    {
        public const string LenovoPage="https://support.lenovo.com/us/en/downloads/ds012808";
        public const string DellPage="https://www.dell.com/support/kbdoc/en-us/000177325/dell-command-update";
        public static string Id(string vendor){if(vendor=="Lenovo")return "VendorUtility.Lenovo";if(vendor=="Dell")return "VendorUtility.DellClassic";throw new ArgumentException("Unsupported vendor utility.");}
        public static string Page(string vendor){Id(vendor);return vendor=="Lenovo"?LenovoPage:DellPage;}
        public static string ResolveLenovo(string html)
        {
            if(!Regex.IsMatch(html??"","System Update",RegexOptions.IgnoreCase)||!Regex.IsMatch(html,"Windows 11",RegexOptions.IgnoreCase))throw new InvalidDataException("Official Lenovo page does not establish System Update / Windows 11 availability.");
            var urls=Regex.Matches(WebUtility.HtmlDecode(html).Replace("\\/","/"),@"https://download\.lenovo\.com/[^\s\""'<>]*system_update_[0-9.]+\.exe",RegexOptions.IgnoreCase).Cast<Match>().Select(m=>m.Value).Distinct().ToArray();
            if(urls.Length!=1)throw new InvalidDataException("Could not resolve one unambiguous official System Update installer. The vendor page may require interactive selection.");
            return urls[0];
        }
        public static string ClassicDetails(string html)
        {
            // Only an explicitly Classic-labelled link is eligible; Universal/ARM links are never substituted.
            foreach(Match m in Regex.Matches(html??"",@"<a\b[^>]*href\s*=\s*[\""'](?<url>[^\""']+)[\""'][^>]*>(?<label>[\s\S]*?)</a>",RegexOptions.IgnoreCase))
            {
                string label=WebUtility.HtmlDecode(Regex.Replace(m.Groups["label"].Value,"<[^>]+>",""));
                if(!Regex.IsMatch(label,@"Command\s*\|?\s*Update.*Classic",RegexOptions.IgnoreCase)||Regex.IsMatch(label,"ARM|Universal",RegexOptions.IgnoreCase))continue;
                var uri=new Uri(new Uri(DellPage),WebUtility.HtmlDecode(m.Groups["url"].Value));
                if(uri.Host=="www.dell.com"&&uri.AbsolutePath.IndexOf("driversdetails",StringComparison.OrdinalIgnoreCase)>=0)return uri.AbsoluteUri;
            }throw new InvalidDataException("An explicit Dell Command | Update Classic driver page could not be resolved. No Universal, Dell Update or SupportAssist package was substituted.");
        }
        public static string ResolveClassic(string html)
        {
            string decoded=WebUtility.HtmlDecode(html??"").Replace("\\/","/");
            if(!Regex.IsMatch(decoded,"Windows 11",RegexOptions.IgnoreCase)||!Regex.IsMatch(decoded,"x64|64-bit|64 bit",RegexOptions.IgnoreCase)||Regex.IsMatch(decoded,@"<title>[^<]*(Universal|ARM|SupportAssist)",RegexOptions.IgnoreCase))throw new InvalidDataException("Classic x64 / Windows 11 compatibility could not be established from the official driver page.");
            var urls=Regex.Matches(decoded,@"https://(?:dl|downloads)\.dell\.com/[^\s\""'<>]+\.exe",RegexOptions.IgnoreCase).Cast<Match>().Select(m=>m.Value).Where(x=>Regex.IsMatch(x,"Dell-Command.*Update",RegexOptions.IgnoreCase)&&!Regex.IsMatch(x,"Universal|ARM|SupportAssist",RegexOptions.IgnoreCase)).Distinct().ToArray();
            if(urls.Length!=1)throw new InvalidDataException("Classic installer URL is unavailable or ambiguous on the official driver page.");return urls[0];
        }
        private static async Task<string> ReadPage(HttpClient client,string id,string page,Action<OutputChunk> output,CancellationToken token)
        {
            using(var response=await DownloadTransport.Get(client,id,page,"Resolve vendor utility",s=>StateWorkflow.Emit(output,s),token).ConfigureAwait(false))using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var memory=new MemoryStream())
            {byte[] b=new byte[16384];int n;while((n=await stream.ReadAsync(b,0,b.Length,token).ConfigureAwait(false))>0){if(memory.Length+n>8*1024*1024)throw new IOException("Vendor metadata exceeds size limit.");memory.Write(b,0,n);}return System.Text.Encoding.UTF8.GetString(memory.ToArray());}
        }
        [DllImport("shell32.dll")]private static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
        public static string DownloadsFolder()
        {var id=new Guid("374DE290-123F-4565-9164-39C4925E467B");IntPtr ptr;int error=SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out ptr);if(error!=0)Marshal.ThrowExceptionForHR(error);try{return Marshal.PtrToStringUni(ptr);}finally{Marshal.FreeCoTaskMem(ptr);}}
        public static async Task<VendorDownloadResult> DownloadAsync(string vendor,Action<OutputChunk> output,CancellationToken token)
        {
            string page=Page(vendor),id=Id(vendor),url=null;
            using(var handler=new HttpClientHandler{AllowAutoRedirect=false})using(var client=new HttpClient(handler){Timeout=TimeSpan.FromMinutes(15)})
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("IT-Toolkit-Roden/1.0.0");
                string resolutionError=null;
                try{using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){deadline.CancelAfter(TimeSpan.FromSeconds(45));var html=await ReadPage(client,id,page,output,deadline.Token).ConfigureAwait(false);if(vendor=="Lenovo"){url=ResolveLenovo(html);StateWorkflow.Emit(output,"Lenovo notice: maintenance ended 2026-10-31; announced EOL 2027-03-31. Review current vendor support/compatibility before manual use.");if(DateTime.UtcNow.Date>=new DateTime(2027,3,31))throw new InvalidOperationException("System Update reached the published end-of-life date; use the official page to review current availability.");}else{string detail=ClassicDetails(html);url=ResolveClassic(await ReadPage(client,id,detail,output,deadline.Token).ConfigureAwait(false));}}}
                catch(OperationCanceledException){if(token.IsCancellationRequested)throw;resolutionError="Official metadata request timed out.";}
                catch(Exception e){resolutionError=e.Message;}
                if(resolutionError!=null)return new VendorDownloadResult{Page=page,Message="No installer downloaded. "+resolutionError+" Opening the official vendor page for manual selection."};
                string root=Path.Combine(DownloadsFolder(),"IT-Toolkit"),directory=Path.Combine(root,vendor+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);using(var identity=WindowsIdentity.GetCurrent())acl.AddAccessRule(new FileSystemAccessRule(identity.User,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));new DirectoryInfo(directory).Create(acl);
                string file=Path.Combine(directory,vendor=="Lenovo"?"Lenovo-System-Update.exe":"Dell-Command-Update-Classic.exe");bool keep=false;
                try{using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))using(var lease=new DirectoryLease(directory)){
                    deadline.CancelAfter(TimeSpan.FromMinutes(15));var downloadToken=deadline.Token;
                    using(var response=await DownloadTransport.Get(client,id,url,"Download vendor utility",s=>StateWorkflow.Emit(output,s),downloadToken).ConfigureAwait(false))using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var target=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
                    {long total=response.Content.Headers.ContentLength??0,received=0;byte[] b=new byte[65536];int n;var clock=Stopwatch.StartNew();long tick=0;while((n=await input.ReadAsync(b,0,b.Length,downloadToken).ConfigureAwait(false))>0){received+=n;if(received>1024L*1024*1024)throw new IOException("Download exceeds 1 GiB.");await target.WriteAsync(b,0,n,downloadToken).ConfigureAwait(false);if(clock.ElapsedMilliseconds-tick>250){tick=clock.ElapsedMilliseconds;if(output!=null)output(new OutputChunk{Channel="status",Text="\rDownloaded "+(received/1048576d).ToString("N1")+" MiB",MeasuredPercent=total>0?(double?)(received*100d/total):null});}}if(received<1024||total>0&&received!=total)throw new IOException("Incomplete vendor installer.");}
                    InternetZone.Mark(file);using(var held=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)){Signature.VerifyPublisher(file,vendor=="Lenovo"?"Lenovo|Lenovo (Beijing) Limited":"Dell Inc.|Dell Technologies Inc.",false);downloadToken.ThrowIfCancellationRequested();keep=true;}
                }return new VendorDownloadResult{Downloaded=true,Path=file,Page=page,Message="Verified official "+(vendor=="Lenovo"?"Lenovo System Update":"Dell Command | Update Classic")+" download saved: "+file+". Not installed or executed. Check vendor model compatibility before manual installation."};}
                finally{if(!keep){try{if(File.Exists(file))File.Delete(file);Directory.Delete(directory,false);}catch{StateWorkflow.Emit(output,"Partial download cleanup failed; inspect "+directory);}}}
            }
        }
    }
}
