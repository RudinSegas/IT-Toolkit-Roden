using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
namespace Toolkit.Software
{
    public static class DownloadTransport
    {
        public static bool Transient(int code){return code==408||code==429||code==500||code==502||code==503||code==504;}
        public static TimeSpan RetryDelay(HttpResponseMessage response,int attempt)
        {
            var retry=response.Headers.RetryAfter;
            double seconds=Math.Pow(2,attempt);
            if(retry!=null)seconds=Math.Max(seconds,retry.Delta.HasValue?retry.Delta.Value.TotalSeconds:retry.Date.HasValue?(retry.Date.Value-DateTimeOffset.UtcNow).TotalSeconds:0);
            // Do not retry sooner than the server permits; an excessive delay is reported instead.
            if(seconds>60)throw new IOException("Server Retry-After exceeds the bounded retry budget; try again later.");
            return TimeSpan.FromSeconds(Math.Max(0,seconds));
        }
        public static async Task<HttpResponseMessage> Get(HttpClient client,string id,string url,string stage,Action<string> log,CancellationToken token,Func<TimeSpan,CancellationToken,Task> delay=null)
        {
            delay=delay??Task.Delay;var uri=new Uri(url);int retries=0;
            for(int redirects=0;redirects<8;)
            {
                token.ThrowIfCancellationRequested();if(!DirectPolicy.Allowed(id,uri))throw new InvalidDataException("Untrusted download origin: "+uri);
                if(log!=null)log(stage+": GET "+uri);
                HttpResponseMessage response=null;
                try{response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);}
                catch(HttpRequestException e){if(retries>=3)throw new IOException(stage+" Failed: "+uri+"; "+e.Message,e);}
                if(response==null){await delay(TimeSpan.FromSeconds(Math.Pow(2,retries++)),token).ConfigureAwait(false);continue;}
                int code=(int)response.StatusCode;
                if(Transient(code)&&retries<3)
                {TimeSpan wait;try{wait=RetryDelay(response,retries++);}finally{response.Dispose();}if(log!=null)log(stage+": HTTP "+code+" at "+uri+"; retry in "+wait.TotalSeconds+"s");await delay(wait,token).ConfigureAwait(false);continue;}
                if(code>=300&&code<400){var next=response.Headers.Location;response.Dispose();if(next==null)throw new IOException(stage+" Failed: redirect missing Location at "+uri);uri=next.IsAbsoluteUri?next:new Uri(uri,next);redirects++;continue;}
                if(!response.IsSuccessStatusCode){response.Dispose();throw new IOException(stage+" Failed: HTTP "+code+" at "+uri);}
                return response;
            }throw new IOException(stage+" Failed: too many redirects at "+uri);
        }
    }
    public static class InternetZone
    {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        public static void Mark(string path)
        {
            string full=Path.GetFullPath(path);
            if(!File.Exists(full)||(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new IOException("MOTW requires an existing regular installer.");
            using(var handle=CreateFile(full+":Zone.Identifier",0x40000000,0,IntPtr.Zero,2,0x80,IntPtr.Zero))
            {if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot preserve Internet zone; installer will not execute.");using(var stream=new FileStream(handle,FileAccess.Write)){byte[] data=Encoding.ASCII.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\n");stream.Write(data,0,data.Length);stream.Flush(true);}}
        }
    }
}
