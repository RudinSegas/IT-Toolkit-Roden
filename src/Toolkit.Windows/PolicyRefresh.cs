using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Toolkit.Core;
namespace Toolkit.Windows
{
    public static class PolicyRefresh
    {
        public static string ReadEvents(DateTime since,Action<OutputChunk> output)
        {
            var text=new StringBuilder();
            try{var query=new EventLogQuery("Microsoft-Windows-GroupPolicy/Operational",PathType.LogName,"*[System[TimeCreated[@SystemTime >= '"+since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")+"']]]"){ReverseDirection=true};
                using(var reader=new EventLogReader(query))for(int i=0;i<80;i++){using(var item=reader.ReadEvent(TimeSpan.FromSeconds(2))){if(item==null)break;string message;try{message=item.FormatDescription();}catch{message=item.ToXml();}string line=item.TimeCreated+" · Event "+item.Id+" · Level="+item.Level+" · "+message;text.AppendLine(line);StateWorkflow.Emit(output,line);}}
            }catch(Exception e){text.AppendLine("Group Policy evidence unavailable: "+e.Message);}
            return text.Length==0?"No new Group Policy events observed. This does not establish successful processing.":text.ToString();
        }
        public static async Task<string> RemoteEvidence(string host,DateTime since,CancellationToken token)
        {
            try{return await BoundedWork.Run("Remote policy evidence "+host,()=>Task.FromResult(ReadRemote(host,since)),12,token).ConfigureAwait(false);}
            catch(OperationCanceledException){throw;}catch(Exception e){return "Remote verification unavailable: "+e.Message;}
        }
        private static string ReadRemote(string host,DateTime since)
        {
            var text=new StringBuilder();using(var session=new EventLogSession(Arguments.Host(host)))
            {var query=new EventLogQuery("Microsoft-Windows-GroupPolicy/Operational",PathType.LogName,"*[System[TimeCreated[@SystemTime >= '"+since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")+"']]]"){Session=session,ReverseDirection=true};
                using(var reader=new EventLogReader(query))for(int i=0;i<30;i++)using(var item=reader.ReadEvent(TimeSpan.FromSeconds(2))){if(item==null)break;string message;try{message=item.FormatDescription();}catch{message=item.ToXml();}text.AppendLine(item.TimeCreated+" Event "+item.Id+": "+message);}}
            return text.Length==0?"No new remote evidence. Completion not established.":text.ToString();
        }
    }
}
