namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.RegularExpressions;

    public enum RequestOutcome
    {
        // Lines are still coming in for it.
        Open,
        Offered,
        NoDeployment,
        NotServiced
    }

    // One PXE boot attempt of a machine, put together from the lines SMSPXE.log wrote for it.
    public sealed class PxeRequest
    {
        public string Server;
        public string Thread;
        public DateTime? Utc;
        public DateTime? LastUtc;
        public string MacAddress;
        public string SmbiosGuid;
        public int? Architecture;
        // The resource the site answered for: a device, or an Unknown Computer object. -1 when the log does not say.
        public long ItemKey = -1;
        // From "device is (not) in the database". Null when the log does not say.
        public bool? InDatabase;
        public bool DuplicateSmbios;
        public bool DuplicateMac;
        public string Netbios;
        public string OfferId;
        public string PackageId;
        public string BootImageId;
        public bool? Mandatory;
        public bool NoAdvertisements;
        public bool NotServiced;
        public readonly List<LogLine> Lines = new List<LogLine>();

        public bool IsUnknownComputer
        {
            get { return ItemKey >= Ids.UnknownComputerStart || InDatabase == false; }
        }

        public RequestOutcome Outcome
        {
            get
            {
                if (!string.IsNullOrEmpty(OfferId)) return RequestOutcome.Offered;
                if (NotServiced) return RequestOutcome.NotServiced;
                if (NoAdvertisements) return RequestOutcome.NoDeployment;
                return RequestOutcome.Open;
            }
        }

        public bool Served
        {
            get { return Outcome == RequestOutcome.Offered; }
        }

        public string Result
        {
            get
            {
                switch (Outcome)
                {
                    case RequestOutcome.Offered: return (Mandatory == true ? "Offered required " : "Offered ") + OfferId;
                    case RequestOutcome.NoDeployment: return "No deployment found";
                    case RequestOutcome.NotServiced: return NoAdvertisements ? "Not serviced: no deployment" : "Not serviced";
                }
                return "No answer in the log";
            }
        }

        public string Identity
        {
            get
            {
                if (ItemKey < 0) return InDatabase == true ? "Known" : InDatabase == false ? "Unknown" : "";
                if (ItemKey >= Ids.UnknownComputerStart) return "Unknown (" + ItemKey.ToString(CultureInfo.InvariantCulture) + ")";
                return "Known (" + ItemKey.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        // DHCP option 93, the client system architecture the PXE client sends.
        public string ArchitectureText
        {
            get
            {
                if (!Architecture.HasValue) return "";
                switch (Architecture.Value)
                {
                    case 0: return "x86 BIOS";
                    case 6: return "x86 UEFI";
                    case 7:
                    case 9: return "x64 UEFI";
                    case 10: return "ARM 32 UEFI";
                    case 11: return "ARM64 UEFI";
                }
                return "Type " + Architecture.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        public string Key
        {
            get { return Engine.Key(MacAddress, SmbiosGuid); }
        }
    }

    // Puts the lines of SMSPXE.log together into PXE requests.
    public static class PxeLog
    {
        static readonly Regex RxPrefix = new Regex(@"^\s*(?<mac>[0-9A-Fa-f]{2}(?:[:-][0-9A-Fa-f]{2}){5})\s*,\s*(?<guid>[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})?\s*:\s*(?<msg>.*)$", RegexOptions.Compiled);
        static readonly Regex RxArch = new Regex(@"Request using architecture\s+(?<a>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex RxAttr = new Regex("(?<n>[A-Za-z]+)=\"(?<v>[^\"]*)\"", RegexOptions.Compiled);
        static readonly Regex RxNetbios = new Regex(@"<NetbiosName>(?<n>[^<]*)</NetbiosName>", RegexOptions.Compiled);
        static readonly Regex RxAdvert = new Regex(@"advertisement\s+(?<id>[A-Z0-9]{3}[0-9A-F]{5})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Lines of one request come within this time of each other.
        static readonly TimeSpan Gap = TimeSpan.FromSeconds(90);
        // A reply or an architecture line belongs to the next request on its thread within this time.
        static readonly TimeSpan Pending = TimeSpan.FromSeconds(30);

        sealed class ThreadState
        {
            public int? Arch;
            public LogLine ArchLine;
            public LogLine Reply;
            public DateTime? Since;
            public PxeRequest Last;
        }

        // Lines must be in the order they were written. Requests come back newest first.
        public static List<PxeRequest> Requests(string server, IList<LogLine> lines)
        {
            List<PxeRequest> all = new List<PxeRequest>();
            Dictionary<string, PxeRequest> open = new Dictionary<string, PxeRequest>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ThreadState> threads = new Dictionary<string, ThreadState>(StringComparer.Ordinal);
            foreach (LogLine l in lines)
            {
                if (l == null || string.IsNullOrEmpty(l.Text)) continue;
                string thread = l.Thread ?? "";
                ThreadState ts;
                if (!threads.TryGetValue(thread, out ts))
                {
                    ts = new ThreadState();
                    threads[thread] = ts;
                }
                Match a = RxArch.Match(l.Text);
                if (a.Success)
                {
                    ts.Arch = int.Parse(a.Groups["a"].Value, CultureInfo.InvariantCulture);
                    ts.ArchLine = l;
                    ts.Reply = null;
                    ts.Since = l.Utc;
                    continue;
                }
                if (l.Text.IndexOf("<ClientIDReply", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // The reply usually comes before the lines with the MAC address. When the request on this thread
                    // has just written lines and has no reply yet, it belongs to that request.
                    PxeRequest last = ts.Last;
                    if (last != null && !HasReply(last) && Near(last.LastUtc, l.Utc, TimeSpan.FromSeconds(5)) && ts.Reply == null && !ts.Arch.HasValue) Reply(last, l);
                    else
                    {
                        ts.Reply = l;
                        if (!ts.Since.HasValue) ts.Since = l.Utc;
                    }
                    continue;
                }
                Match p = RxPrefix.Match(l.Text);
                if (!p.Success) continue;
                string mac = Ids.Mac(p.Groups["mac"].Value);
                string guid = p.Groups["guid"].Success ? Ids.Guid(p.Groups["guid"].Value) : null;
                string msg = p.Groups["msg"].Value.Trim();
                string key = server + "|" + mac + "|" + guid;
                PxeRequest r;
                // A new architecture line or reply on the thread starts a new boot of the same machine.
                bool fresh = (ts.Arch.HasValue || ts.Reply != null) && Near(ts.Since, l.Utc, Pending);
                if (!open.TryGetValue(key, out r) || r.NotServiced || !Near(r.LastUtc, l.Utc, Gap) || (fresh && (HasReply(r) || r.Architecture.HasValue)))
                {
                    r = new PxeRequest();
                    r.Server = server;
                    r.Thread = thread;
                    r.MacAddress = mac;
                    r.SmbiosGuid = guid;
                    r.Utc = l.Utc;
                    open[key] = r;
                    all.Add(r);
                }
                if (Near(ts.Since, l.Utc, Pending))
                {
                    if (ts.Arch.HasValue && !r.Architecture.HasValue)
                    {
                        r.Architecture = ts.Arch;
                        if (ts.ArchLine != null)
                        {
                            r.Lines.Insert(0, ts.ArchLine);
                            if (ts.ArchLine.Utc.HasValue && (!r.Utc.HasValue || ts.ArchLine.Utc.Value < r.Utc.Value)) r.Utc = ts.ArchLine.Utc;
                        }
                    }
                    if (ts.Reply != null && !HasReply(r)) Reply(r, ts.Reply);
                }
                ts.Arch = null;
                ts.ArchLine = null;
                ts.Reply = null;
                ts.Since = null;
                ts.Last = r;
                r.Lines.Add(l);
                r.LastUtc = l.Utc ?? r.LastUtc;
                Message(r, msg);
            }
            all.Reverse();
            return all;
        }

        static bool HasReply(PxeRequest r)
        {
            return r.ItemKey >= 0 || r.OfferId != null;
        }

        static bool Near(DateTime? a, DateTime? b, TimeSpan span)
        {
            if (!a.HasValue || !b.HasValue) return true;
            return Math.Abs((b.Value - a.Value).TotalSeconds) <= span.TotalSeconds;
        }

        static void Reply(PxeRequest r, LogLine l)
        {
            r.Lines.Add(l);
            foreach (Match m in RxAttr.Matches(l.Text))
            {
                string v = m.Groups["v"].Value;
                switch (m.Groups["n"].Value)
                {
                    case "ItemKey":
                        long k;
                        if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out k)) r.ItemKey = k;
                        break;
                    case "DuplicateSMBIOS": r.DuplicateSmbios = v == "1"; break;
                    case "DuplicateMACAddress": r.DuplicateMac = v == "1"; break;
                    case "OfferID": if (v.Length > 0) r.OfferId = v; break;
                    case "PkgID": if (v.Length > 0) r.PackageId = v; break;
                    case "BootImageID": if (v.Length > 0) r.BootImageId = v; break;
                    case "Mandatory": if (v.Length > 0) r.Mandatory = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                }
            }
            Match n = RxNetbios.Match(l.Text);
            if (n.Success && n.Groups["n"].Value.Trim().Length > 0) r.Netbios = n.Groups["n"].Value.Trim();
            if (r.LastUtc == null || (l.Utc.HasValue && l.Utc.Value > r.LastUtc.Value)) r.LastUtc = l.Utc;
            if (r.Utc == null || (l.Utc.HasValue && l.Utc.Value < r.Utc.Value)) r.Utc = l.Utc;
        }

        static void Message(PxeRequest r, string msg)
        {
            string m = msg.ToLowerInvariant();
            if (m.Contains("not serviced")) r.NotServiced = true;
            if (m.Contains("no advertisements found") || m.Contains("no advertisement found")) r.NoAdvertisements = true;
            if (m.Contains("is not in the database")) r.InDatabase = false;
            else if (m.Contains("is in the database")) r.InDatabase = true;
            Match a = RxAdvert.Match(msg);
            if (a.Success && !m.Contains("no advertisement"))
            {
                if (r.OfferId == null) r.OfferId = a.Groups["id"].Value.ToUpperInvariant();
                if (m.Contains("optional")) r.Mandatory = false;
                else if (m.Contains("mandatory") || m.Contains("required")) r.Mandatory = true;
            }
        }
    }

    // The lines PxeVice has read from SMSPXE.log, per PXE distribution point, and the requests in them.
    public sealed class PxeLogStore
    {
        // Lines kept per server. Older lines are dropped first.
        public const int MaxLines = 60000;
        // The servers are read on their own threads, so everything here is locked.
        readonly object gate = new object();
        readonly Dictionary<string, List<LogLine>> lines = new Dictionary<string, List<LogLine>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, bool> reading = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public void Add(string server, IList<LogLine> more)
        {
            lock (gate)
            {
                List<LogLine> l;
                if (!lines.TryGetValue(server, out l))
                {
                    l = new List<LogLine>();
                    lines[server] = l;
                }
                l.AddRange(more);
                if (l.Count > MaxLines) l.RemoveRange(0, l.Count - MaxLines);
            }
        }

        public void Note(string server, string note)
        {
            lock (gate)
            {
                if (note == null) notes.Remove(server);
                else notes[server] = note;
            }
        }

        public List<string> NoteList()
        {
            lock (gate) return new List<string>(notes.Values);
        }

        // Marks a server as being read. False when an earlier read of it has not finished.
        public bool Begin(string server)
        {
            lock (gate)
            {
                if (reading.ContainsKey(server)) return false;
                reading[server] = true;
                return true;
            }
        }

        public void End(string server)
        {
            lock (gate) reading.Remove(server);
        }

        public List<string> Busy()
        {
            lock (gate) return new List<string>(reading.Keys);
        }

        public int LineCount
        {
            get { lock (gate) { int n = 0; foreach (List<LogLine> l in lines.Values) n += l.Count; return n; } }
        }

        public List<PxeRequest> Requests()
        {
            List<KeyValuePair<string, List<LogLine>>> copy = new List<KeyValuePair<string, List<LogLine>>>();
            lock (gate) foreach (KeyValuePair<string, List<LogLine>> kv in lines) copy.Add(new KeyValuePair<string, List<LogLine>>(kv.Key, new List<LogLine>(kv.Value)));
            List<PxeRequest> all = new List<PxeRequest>();
            foreach (KeyValuePair<string, List<LogLine>> kv in copy) all.AddRange(PxeLog.Requests(kv.Key, kv.Value));
            all.Sort(delegate (PxeRequest a, PxeRequest b)
            {
                DateTime x = a.Utc ?? DateTime.MinValue, y = b.Utc ?? DateTime.MinValue;
                return y.CompareTo(x);
            });
            return all;
        }
    }
}
