using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Toolkit.Core;
using Toolkit.Windows;

namespace Toolkit.Reporting
{
    public static class DiagnosticReports
    {
        public static string Redact(string text,IEnumerable<string> identities)
        {
            string value=SafeText.Redact(text??"");
            foreach(string identity in identities.Where(x=>!String.IsNullOrWhiteSpace(x)&&x.Length>=3&&x!="Unavailable"&&x!="Unknown").Distinct().OrderByDescending(x=>x.Length))value=Regex.Replace(value,Regex.Escape(identity),"[redacted]",RegexOptions.IgnoreCase);
            value=Regex.Replace(value,@"(?i)\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b|\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b|\b[0-9a-f]{12}\b", "[address]");
            value=Regex.Replace(value,@"(?i)(?:[0-9a-f]{0,4}:){2,}[0-9a-f:%]*", "[IPv6]");
            value=Regex.Replace(value,@"(?i)\b[A-Z]:\\[^\r\n<>""|]*|\\\\[^\s<>""|]+", "[path]");
            return value;
        }
        public static DiagnosticSnapshot[] Prepare(IEnumerable<DiagnosticSnapshot> snapshots,bool redact,IEnumerable<string> identities)
        {
            return snapshots.Select(s=>new DiagnosticSnapshot{Section=s.Section,Timestamp=s.Timestamp,Summary=redact?Redact(s.Summary,identities):SafeText.Redact(s.Summary),Records=s.Records.Select(r=>new DiagnosticRecord{Section=r.Section,Timestamp=r.Timestamp,State=r.State,Name=redact?Redact(r.Name,identities):SafeText.Redact(r.Name),Value=redact&&(Regex.IsMatch(r.Name,@"(?i)serial|hardware.?id|device.?id|user|domain|computer|MAC|address|DNS|gateway|command|path|event|operation"))?"[redacted field]":redact?Redact(r.Value,identities):SafeText.Redact(r.Value)}).ToList()}).ToArray();
        }
        public static string Json(IEnumerable<DiagnosticSnapshot> snapshots) {return new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Serialize(new{ToolkitVersion=Release.Version,GeneratedAt=DateTimeOffset.Now.ToString("o"),Sections=snapshots});}
        public static string Html(IEnumerable<DiagnosticSnapshot> snapshots)
        {
            var b=new StringBuilder("<!doctype html><html lang='en'><meta charset='utf-8'><title>IT Toolkit by Roden Segas diagnostic report</title><style>body{font:15px system-ui;margin:40px;background:#f6f8fc;color:#172238}h1,h2{color:#243e6a}table{border-collapse:collapse;width:100%;background:white;margin-bottom:30px}td,th{padding:10px;border:1px solid #dce2eb;text-align:left;white-space:pre-wrap;overflow-wrap:anywhere}th{background:#eaf0fa}.meta{color:#506078}</style><h1>IT Toolkit by Roden Segas · "+Release.Version+"</h1><p class='meta'>Generated "+DateTimeOffset.Now.ToString("o")+". Observations are point-in-time; unavailable evidence does not establish health.</p>");
            foreach(var s in snapshots){b.Append("<h2>").Append(WebUtility.HtmlEncode(s.Section)).Append("</h2><p>").Append(WebUtility.HtmlEncode(s.Timestamp)).Append("</p><p>").Append(WebUtility.HtmlEncode(s.Summary)).Append("</p><table><tr><th>Item</th><th>Result</th><th>State</th></tr>");foreach(var r in s.Records)b.Append("<tr><td>").Append(WebUtility.HtmlEncode(r.Name)).Append("</td><td>").Append(WebUtility.HtmlEncode(r.Value)).Append("</td><td>").Append(WebUtility.HtmlEncode(r.State)).Append("</td></tr>");b.Append("</table>");}return b.Append("</html>").ToString();
        }
    }
}
