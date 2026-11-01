using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
namespace Toolkit.Windows
{
    public sealed class ServiceRow
    {
        public string Name{get;set;}public string DisplayName{get;set;}public string Status{get;set;}public string Startup{get;set;}public string Description{get;set;}public bool AcceptStop{get;set;}
        public bool CanStart{get{return Status=="Stopped"&&Startup!="Disabled"&&ServicePolicy.ValidName(Name);}}
        public bool CanStop{get{return Status=="Running"&&AcceptStop&&ServicePolicy.ValidName(Name);}}
        public bool CanRestart{get{return CanStop&&Startup!="Disabled";}}
        public string Explanation{get{return !ServicePolicy.ValidName(Name)?"This protected or unsupported service is read-only in the toolkit.":Status=="Running"&&!AcceptStop?"Windows reports that this service does not accept Stop.":Startup=="Disabled"?"Startup is Disabled. This toolkit does not change startup configuration.":Status!="Running"&&Status!="Stopped"?"Service is transitioning or its state is unknown; refresh before acting.":Status=="Running"?"Already Running. Stop/Restart are available only if Windows permits them.":"Already Stopped. Start is available if enabled.";}}
    }
    public static class ServicePolicy
    {
        public static bool ValidName(string name){return Regex.IsMatch(name??"",@"\A[A-Za-z0-9_.$-]{1,256}\z")&&!new[]{"WinDefend","WdNisSvc","Sense","SecurityHealthService","RpcSs","DcomLaunch","SamSs","BFE","MpsSvc"}.Contains(name,StringComparer.OrdinalIgnoreCase);}
        public static bool Operation(string value){if(value==null)return false;var parts=value.Split(':');return parts.Length==2&&new[]{"StartService","StopService","RestartService"}.Contains(parts[0])&&ValidName(parts[1]);}
    }
    public sealed class EventRow
    {
        public string Log{get;set;}public long RecordId{get;set;}public string Time{get;set;}public string Severity{get;set;}public string Source{get;set;}public int Id{get;set;}public string ShortDescription{get;set;}
    }
    public static class ServiceEventBrowser
    {
        public static readonly string[] Logs={"System","Application","Security","Microsoft-Windows-WindowsUpdateClient/Operational","Microsoft-Windows-GroupPolicy/Operational","Microsoft-Windows-DeviceManagement-Enterprise-Diagnostics-Provider/Admin"};
        public static List<ServiceRow> Services(CancellationToken token)
        {
            var result=new List<ServiceRow>();using(var q=new ManagementObjectSearcher(@"root\cimv2","SELECT Name,DisplayName,State,StartMode,Description,AcceptStop FROM Win32_Service",new EnumerationOptions{Timeout=TimeSpan.FromSeconds(10),ReturnImmediately=false}))using(var rows=q.Get())foreach(ManagementObject r in rows)using(r){token.ThrowIfCancellationRequested();if(result.Count>=2000)throw new InvalidOperationException("Service inventory exceeded the bounded limit.");result.Add(new ServiceRow{Name=Convert.ToString(r["Name"]),DisplayName=Convert.ToString(r["DisplayName"]),Status=Convert.ToString(r["State"]),Startup=Convert.ToString(r["StartMode"]),Description=Convert.ToString(r["Description"]),AcceptStop=r["AcceptStop"]!=null&&Convert.ToBoolean(r["AcceptStop"])});}return result.OrderBy(x=>x.DisplayName).ToList();
        }
        public static string EventQuery(string severity,int days,string eventId,string source,long before)
        {
            if(days<1||days>30)throw new ArgumentException("Choose 1–30 days.");
            string levels=severity=="Critical"?"Level=1":severity=="Error"?"Level=2":severity=="Warning"?"Level=3":severity=="Information"?"Level=4":severity=="Both"?"Level=1 or Level=2 or Level=3":severity=="All"?"Level>=0":null;
            if(levels==null)throw new ArgumentException("Unsupported event severity.");
            string filter="("+levels+") and TimeCreated[timediff(@SystemTime)<="+((long)days*86400000).ToString(CultureInfo.InvariantCulture)+"]";
            if(!String.IsNullOrWhiteSpace(eventId)){int id;if(!Int32.TryParse(eventId,out id)||id<0||id>65535)throw new ArgumentException("Event ID must be 0–65535, or blank.");filter+=" and EventID="+id;}
            if(!String.IsNullOrWhiteSpace(source)){if(source.Length>128||Regex.IsMatch(source,"['\"\\r\\n\\x00]"))throw new ArgumentException("Source is an exact provider name, up to 128 characters, without quotes.");filter+=" and Provider[@Name='"+source+"']";}
            if(before>0)filter+=" and EventRecordID<"+before;return "*[System["+filter+"]]";
        }
        public static string Level(byte? level){return level==1?"Critical":level==2?"Error":level==3?"Warning":level==4?"Information":level==5?"Verbose":level==0?"Audit / Log always":"Unknown";}
        public static List<EventRow> Events(string log,string query,int maximum,CancellationToken token)
        {
            if(!Logs.Contains(log))throw new ArgumentException("Log not allowlisted.");if(maximum<1||maximum>500)throw new ArgumentException("Choose 1–500 records per page.");var rows=new List<EventRow>();
            using(var reader=new EventLogReader(new EventLogQuery(log,PathType.LogName,query){ReverseDirection=true}))for(int i=0;i<maximum;i++)using(var record=reader.ReadEvent(TimeSpan.FromSeconds(3)))
            {token.ThrowIfCancellationRequested();if(record==null)break;string text;try{text=record.FormatDescription()??"Message resource unavailable";}catch{text="Message resource unavailable; select for XML details";}text=Regex.Replace(text,@"\s+"," ");rows.Add(new EventRow{Log=log,RecordId=record.RecordId??0,Time=record.TimeCreated.HasValue?record.TimeCreated.Value.ToString("yyyy-MM-dd HH:mm:ss"):"Unknown",Severity=Level(record.Level),Source=record.ProviderName,Id=record.Id,ShortDescription=text.Length>240?text.Substring(0,240)+"…":text});}return rows;
        }
        public static string Details(EventRow row)
        {
            if(row==null||!Logs.Contains(row.Log)||row.RecordId<=0)throw new ArgumentException("Select an event with a valid record ID.");
            using(var reader=new EventLogReader(new EventLogQuery(row.Log,PathType.LogName,"*[System[EventRecordID="+row.RecordId+"]]")))using(var r=reader.ReadEvent(TimeSpan.FromSeconds(3)))
            {if(r==null)return "Event no longer available (the log may have rolled over or been cleared).";string text;try{text=r.FormatDescription();}catch{text="Localized message unavailable.";}return text+"\r\n\r\nEvent XML\r\n"+r.ToXml();}
        }
    }
}
