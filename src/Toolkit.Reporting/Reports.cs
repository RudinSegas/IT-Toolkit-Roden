using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Toolkit.Core;

namespace Toolkit.Reporting
{
    public static class Reports
    {
        public static string Html(InventorySnapshot snapshot, IEnumerable<string> results)
        {
            var body = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>IT Toolkit by Roden Segas report</title><style>body{font:15px Segoe UI,Arial;background:#101624;color:#e9eef8;margin:32px}section{background:#181f30;padding:24px;margin:20px 0;border-radius:12px}pre{white-space:pre-wrap;overflow-wrap:anywhere}h1,h2{color:#a8c0ff}@media print{body,section{background:white;color:black}}</style><h1>IT Toolkit by Roden Segas v1.0.0</h1>");
            body.Append("<p>Generated ").Append(WebUtility.HtmlEncode(DateTimeOffset.Now.ToString("o"))).Append(". Contains local device/account/network information and explicitly exported results; review before sharing.</p><section><h2>Inventory snapshot</h2><pre>");
            body.Append(WebUtility.HtmlEncode(snapshot == null ? "No inventory collected." : snapshot.ToString())).Append("</pre></section>");
            foreach (var result in results) body.Append("<section><h2>Completed operation</h2><pre>").Append(WebUtility.HtmlEncode(result)).Append("</pre></section>");
            return body.Append("</html>").ToString();
        }
        public static void WriteHtml(string path, InventorySnapshot snapshot, IEnumerable<string> results)
        { File.WriteAllText(path, Html(snapshot, results), new UTF8Encoding(false)); }
    }
}
