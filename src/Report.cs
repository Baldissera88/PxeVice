namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net;
    using System.Text;

    // A report for a ticket: the machines, their check, their PXE boots and the changes made. It follows the light or dark
    // mode of the browser and has a print layout.
    public static class Report
    {
        const string Css = @"
:root{--bg:#f5f6f8;--card:#fff;--text:#1b1d21;--text2:#4a4f57;--text3:#6b717b;--line:#e2e5ea;--accent:#0a5fbf;--err:#b42318;--errbg:#fdecea;--warn:#8a5a00;--warnbg:#fff4dc;--ok:#1a7f37;--okbg:#e6f4ea;--info:#3b4a5a;--infobg:#eef1f5}
@media (prefers-color-scheme:dark){:root{--bg:#16181c;--card:#1f2228;--text:#eceef1;--text2:#c2c6cc;--text3:#959ba4;--line:#33373e;--accent:#5aa9ff;--err:#ff8a80;--errbg:#3a1d1b;--warn:#ffcf66;--warnbg:#3a2e14;--ok:#7ee2a0;--okbg:#17321f;--info:#c2cad4;--infobg:#262b33}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 'Segoe UI',system-ui,sans-serif}
main{max-width:1100px;margin:0 auto;padding:28px 20px 48px}
h1{font-size:24px;margin:0 0 2px}h2{font-size:18px;margin:32px 0 10px}h3{font-size:15px;margin:0 0 6px}
.sub{color:var(--text3);font-size:13px}
.card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:16px 18px;margin:10px 0}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:4px 24px}
.k{color:var(--text3);font-size:12px}.t{font-size:14px}.v{font-family:Consolas,'Cascadia Mono',monospace;font-size:13px;word-break:break-all}
.f{border-left:4px solid var(--line);padding:10px 14px;margin:8px 0;border-radius:6px;background:var(--infobg)}
.f.Blocks{border-color:var(--err);background:var(--errbg)}.f.Warning{border-color:var(--warn);background:var(--warnbg)}.f.Ok{border-color:var(--ok);background:var(--okbg)}
.tag{display:inline-block;font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.04em;margin-right:8px}
.Blocks .tag{color:var(--err)}.Warning .tag{color:var(--warn)}.Ok .tag{color:var(--ok)}.Info .tag{color:var(--info)}
.todo{margin-top:4px}.todo b{color:var(--text2)}
pre{margin:6px 0 0;padding:8px 10px;background:var(--card);border:1px solid var(--line);border-radius:6px;font:12px/1.45 Consolas,'Cascadia Mono',monospace;white-space:pre-wrap;word-break:break-all}
table{width:100%;border-collapse:collapse;font-size:13px}th,td{text-align:left;padding:6px 8px;border-bottom:1px solid var(--line);vertical-align:top}th{color:var(--text3);font-weight:600}
.bad{color:var(--err)}.good{color:var(--ok)}
footer{margin-top:40px;color:var(--text3);font-size:12px}
@media print{body{background:#fff;color:#000}.card,.f{break-inside:avoid}main{max-width:none;padding:0}}";

        static string H(string s)
        {
            return WebUtility.HtmlEncode(s ?? "");
        }

        public static string Html(SiteInfo info, IList<Machine> machines, Dictionary<string, List<Finding>> checks, IList<Activity> activity, DateTime now, string title)
        {
            StringBuilder b = new StringBuilder();
            b.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            b.Append("<title>").Append(H(title)).Append("</title><style>").Append(Css).Append("</style></head><body><main>");
            b.Append("<h1>").Append(H(title)).Append("</h1>");
            b.Append("<div class=\"sub\">").Append(H(info.Describe(" · "))).Append(" · ").Append(H(Txt.Local(now))).Append(" · ").Append(H(Cli.UserName)).Append("</div>");

            foreach (Machine m in machines)
            {
                List<Finding> f;
                if (checks == null || !checks.TryGetValue(m.Key ?? "", out f)) f = null;
                b.Append("<h2>").Append(H(m.Name)).Append("</h2>");
                b.Append("<div class=\"card\"><div class=\"grid\">");
                Field(b, "Found", m.Found, false);
                Field(b, "MAC addresses", string.Join(", ", m.AllMacs.ToArray()), true);
                Field(b, "SMBIOS GUID", m.SmbiosGuid, true);
                Field(b, "Device records", m.ResourceIds, true);
                Field(b, "Deployments", m.Deployments, false);
                Field(b, "Last seen", Txt.Local(m.LastSeenUtc), false);
                b.Append("</div></div>");
                if (f != null)
                {
                    b.Append("<h3>PXE check</h3>");
                    foreach (Finding x in f)
                    {
                        b.Append("<div class=\"f ").Append(x.Severity).Append("\"><span class=\"tag\">").Append(H(x.SeverityText)).Append("</span><b>").Append(H(x.Title)).Append("</b>");
                        if (!string.IsNullOrEmpty(x.What)) b.Append("<div>").Append(H(x.What)).Append("</div>");
                        if (!string.IsNullOrEmpty(x.ToDo)) b.Append("<div class=\"todo\"><b>To do:</b> ").Append(H(x.ToDo)).Append("</div>");
                        if (x.Evidence.Count > 0) b.Append("<pre>").Append(H(string.Join("\n", x.Evidence.ToArray()))).Append("</pre>");
                        b.Append("</div>");
                    }
                }
                if (m.Requests.Count > 0)
                {
                    b.Append("<h3 style=\"margin-top:18px\">PXE boots</h3><div class=\"card\"><table><tr><th>Time</th><th>Server</th><th>Architecture</th><th>Answered for</th><th>Result</th></tr>");
                    foreach (PxeRequest r in m.Requests)
                    {
                        b.Append("<tr><td>").Append(H(Txt.Local(r.Utc))).Append("</td><td>").Append(H(r.Server)).Append("</td><td>").Append(H(r.ArchitectureText)).Append("</td><td>").Append(H(r.Identity));
                        b.Append("</td><td class=\"").Append(r.Served ? "good" : r.Outcome == RequestOutcome.Open ? "" : "bad").Append("\">").Append(H(r.Served ? "Offered " + m.DeploymentText(r.OfferId) : r.Result)).Append("</td></tr>");
                    }
                    b.Append("</table></div>");
                }
            }

            if (activity != null && activity.Count > 0)
            {
                b.Append("<h2>Changes</h2><div class=\"card\"><table><tr><th>Time</th><th>Machine</th><th>Change</th><th>Result</th><th>By</th></tr>");
                foreach (Activity a in activity)
                {
                    b.Append("<tr><td>").Append(H(Txt.Local(a.TimeUtc))).Append("</td><td>").Append(H(a.Machine)).Append("</td><td>").Append(H(a.Action));
                    b.Append("</td><td class=\"").Append(a.Ok ? "" : "bad").Append("\">").Append(H((a.Ok ? "" : "Failed: ") + a.Result)).Append("</td><td>").Append(H(a.User)).Append("</td></tr>");
                }
                b.Append("</table></div>");
            }
            b.Append("<footer>PxeVice ").Append(H(Cli.Version)).Append(" · ").Append(H(Cli.Developer)).Append("</footer>");
            b.Append("</main></body></html>");
            return b.ToString();
        }

        static void Field(StringBuilder b, string k, string v, bool mono)
        {
            if (string.IsNullOrEmpty(v)) return;
            b.Append("<div><div class=\"k\">").Append(H(k)).Append("</div><div class=\"").Append(mono ? "v" : "t").Append("\">").Append(H(v)).Append("</div></div>");
        }

        static string Csv(string s)
        {
            s = s ?? "";
            if (s.IndexOfAny(new char[] { ',', '"', '\r', '\n', ';' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        public static string RequestsCsv(IList<PxeRequest> requests, Dictionary<string, string> names)
        {
            StringBuilder b = new StringBuilder();
            b.Append("Time,Server,MAC address,SMBIOS GUID,Architecture,Item key,Known,Duplicate SMBIOS,Duplicate MAC,Result,Deployment,Task sequence,NetBIOS name\r\n");
            foreach (PxeRequest r in requests)
            {
                string name;
                if (names == null || r.OfferId == null || !names.TryGetValue(r.OfferId, out name)) name = "";
                b.Append(Csv(Txt.Local(r.Utc))).Append(',').Append(Csv(r.Server)).Append(',').Append(Csv(r.MacAddress)).Append(',').Append(Csv(r.SmbiosGuid)).Append(',');
                b.Append(Csv(r.ArchitectureText)).Append(',').Append(r.ItemKey >= 0 ? r.ItemKey.ToString(CultureInfo.InvariantCulture) : "").Append(',');
                b.Append(r.IsUnknownComputer ? "No" : r.ItemKey >= 0 ? "Yes" : "").Append(',').Append(r.DuplicateSmbios ? "Yes" : "No").Append(',').Append(r.DuplicateMac ? "Yes" : "No").Append(',');
                b.Append(Csv(r.Result)).Append(',').Append(Csv(r.OfferId)).Append(',').Append(Csv(name)).Append(',').Append(Csv(r.Netbios)).Append("\r\n");
            }
            return b.ToString();
        }
    }
}
