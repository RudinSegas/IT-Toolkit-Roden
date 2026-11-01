using System;
using System.Threading;
using System.Threading.Tasks;
namespace Toolkit.Core
{
    public enum ChangeVerdict { AlreadyConfigured, SuccessfullyChanged, Failed, RestartRequired, VerificationInconclusive }
    public sealed class StateEvidence
    {
        public string Description;
        public bool? Desired;
        public bool PendingRestart;
    }
    public static class StateWorkflow
    {
        public static ChangeVerdict Decide(StateEvidence state, bool explicitRepair)
        {
            if(state.PendingRestart)return ChangeVerdict.RestartRequired;
            return state.Desired==true&&!explicitRepair?ChangeVerdict.AlreadyConfigured:ChangeVerdict.VerificationInconclusive;
        }
        public static ChangeVerdict Verify(int code,StateEvidence after)
        {
            if(code!=0&&code!=3010&&code!=1641)return ChangeVerdict.Failed;
            if(after.PendingRestart||code==3010||code==1641)return ChangeVerdict.RestartRequired;
            if(after.Desired==true)return ChangeVerdict.SuccessfullyChanged;
            return after.Desired==false?ChangeVerdict.Failed:ChangeVerdict.VerificationInconclusive;
        }
        public static ProcessResult Result(ChangeVerdict verdict,string summary,int? code=null)
        {return new ProcessResult{Verdict=verdict.ToString(),Summary=summary,NativeExitCode=code,ExitCode=verdict==ChangeVerdict.Failed?code.GetValueOrDefault(1)==0?1:code.GetValueOrDefault(1):verdict==ChangeVerdict.RestartRequired?3010:0};}
        public static void Emit(Action<OutputChunk> output,string text)
        {if(output==null)return; text+="\r\n";for(int i=0;i<text.Length;i+=1024)output(new OutputChunk{Channel="status",Text=text.Substring(i,Math.Min(1024,text.Length-i))});}
    }
}
