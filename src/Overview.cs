namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    // What the Overview page says, worked out from what PxeVice read. No window is needed to test it.
    public sealed class OverviewData
    {
        public int Boots, Served, NotServiced;
        public int Machines, NeedAttention;
        public int Hours = 24;
        // The machines whose last PXE boot was not serviced first, then the others with something left behind.
        public readonly List<Machine> Attention = new List<Machine>();
        // What is left behind, with how many machines have it, most common first.
        public readonly List<KeyValuePair<string, int>> Reasons = new List<KeyValuePair<string, int>>();

        public string Headline
        {
            get
            {
                if (NeedAttention == 0) return "Nothing needs a look";
                return NeedAttention + (NeedAttention == 1 ? " machine needs a look" : " machines need a look");
            }
        }

        public string Lead
        {
            get
            {
                string when = Hours == 1 ? "In the last hour" : Hours % 24 == 0 ? "In the last " + (Hours == 24 ? "24 hours" : Hours / 24 + " days") : "In the last " + Hours + " hours";
                if (Boots == 0) return when + ", SMSPXE.log shows no PXE boot.";
                return when + ", " + Boots + (Boots == 1 ? " PXE boot" : " PXE boots") + ": " + Served + " served, " + NotServiced + " not serviced.";
            }
        }
    }

    public static class PxeOverview
    {
        public static OverviewData From(SiteData d, DateTime nowUtc, int hours)
        {
            OverviewData o = new OverviewData();
            o.Hours = hours < 1 ? 24 : hours;
            DateTime since = nowUtc.AddHours(-o.Hours);
            if (d == null) return o;
            // One boot is one request. A request without a time is left out, since it cannot be placed in the window.
            foreach (PxeRequest r in d.Requests)
            {
                if (!r.Utc.HasValue || r.Utc.Value < since || r.Utc.Value > nowUtc.AddMinutes(5)) continue;
                o.Boots++;
                if (r.Served) o.Served++;
                else if (r.Outcome == RequestOutcome.NotServiced || r.Outcome == RequestOutcome.NoDeployment) o.NotServiced++;
            }
            o.Machines = d.Machines.Count;
            List<Machine> bad = new List<Machine>(), rest = new List<Machine>();
            Dictionary<string, int> reasons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<string> order = new List<string>();
            foreach (Machine m in d.Machines)
            {
                (m.NotServiced ? bad : rest).Add(m);
                string why = m.Found;
                if (why == "Nothing") continue;
                if (!reasons.ContainsKey(why)) { reasons[why] = 0; order.Add(why); }
                reasons[why]++;
            }
            Comparison<Machine> newest = delegate (Machine a, Machine b) { return Nullable.Compare(b.LastSeenUtc, a.LastSeenUtc); };
            bad.Sort(newest);
            rest.Sort(newest);
            o.Attention.AddRange(bad);
            o.Attention.AddRange(rest);
            o.NeedAttention = o.Attention.Count;
            foreach (string k in order) o.Reasons.Add(new KeyValuePair<string, int>(k, reasons[k]));
            o.Reasons.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
            return o;
        }
    }

    // The machines opened last, kept per Windows user.
    public sealed class RecentMachine
    {
        public string Key, Name;
    }

    public static class RecentStore
    {
        public const string Pref = "RecentMachines";
        public const int Max = 8;

        public static List<RecentMachine> Load(ISettingStore s)
        {
            List<RecentMachine> l = new List<RecentMachine>();
            string raw = s.Get(Pref);
            if (string.IsNullOrEmpty(raw)) return l;
            foreach (string line in raw.Split('\n'))
            {
                string[] p = line.Split('\t');
                if (p.Length != 2 || p[0].Length == 0 || p[1].Length == 0) continue;
                RecentMachine r = new RecentMachine();
                r.Key = p[0]; r.Name = p[1];
                l.Add(r);
                if (l.Count == Max) break;
            }
            return l;
        }

        // The machine goes first. An older entry with the same key is dropped.
        public static void Push(ISettingStore s, string key, string name)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(name)) return;
            List<RecentMachine> l = Load(s);
            l.RemoveAll(delegate (RecentMachine x) { return x.Key == key; });
            RecentMachine r = new RecentMachine();
            r.Key = key.Replace('\t', ' ').Replace('\n', ' ');
            r.Name = name.Replace('\t', ' ').Replace('\n', ' ');
            l.Insert(0, r);
            if (l.Count > Max) l.RemoveRange(Max, l.Count - Max);
            StringBuilder b = new StringBuilder();
            foreach (RecentMachine x in l) { if (b.Length > 0) b.Append('\n'); b.Append(x.Key).Append('\t').Append(x.Name); }
            s.Set(Pref, b.ToString());
        }

        public static void Clear(ISettingStore s)
        {
            s.Set(Pref, null);
        }
    }

    // Where the user was: a tab and maybe a machine.
    public sealed class NavEntry
    {
        public int Tab;
        public string Key;

        public NavEntry(int tab, string key)
        {
            Tab = tab;
            Key = key;
        }

        public bool Same(NavEntry o)
        {
            return o != null && o.Tab == Tab && string.Equals(o.Key ?? "", Key ?? "", StringComparison.Ordinal);
        }
    }

    // Back and Forward, like a browser: a new place cuts off what was forward.
    public sealed class NavHistory
    {
        public const int Max = 50;
        readonly List<NavEntry> list = new List<NavEntry>();
        int at = -1;

        public NavEntry Current
        {
            get { return at >= 0 ? list[at] : null; }
        }

        public bool CanBack { get { return at > 0; } }
        public bool CanForward { get { return at >= 0 && at < list.Count - 1; } }
        public int Count { get { return list.Count; } }

        public void Go(NavEntry e)
        {
            if (e == null || (Current != null && Current.Same(e))) return;
            if (at < list.Count - 1) list.RemoveRange(at + 1, list.Count - at - 1);
            list.Add(e);
            if (list.Count > Max) list.RemoveAt(0);
            at = list.Count - 1;
        }

        public NavEntry Back()
        {
            if (!CanBack) return null;
            return list[--at];
        }

        public NavEntry Forward()
        {
            if (!CanForward) return null;
            return list[++at];
        }
    }

    // What a search text is, so a MAC address or an SMBIOS GUID is found however it was copied.
    public enum QueryKind { Empty, Mac, Guid, Text }

    public static class MachineSearch
    {
        // A GUID may come with braces, such as {4C4C4544-...}, or in lower case. Ids.Guid does the rest.
        public static QueryKind Kind(string q, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(q)) return QueryKind.Empty;
            string t = q.Trim();
            string guid = Ids.Guid(t.Trim('{', '}', ' '));
            if (guid != null) { normalized = guid; return QueryKind.Guid; }
            // A MAC address is the whole text, not a piece of a longer one such as a GUID.
            string hex = t.Replace(":", "").Replace("-", "").Replace(".", "");
            if (hex.Length == 12 && System.Text.RegularExpressions.Regex.IsMatch(hex, "^[0-9A-Fa-f]{12}$"))
            {
                normalized = Ids.Mac(hex);
                if (normalized != null) return QueryKind.Mac;
            }
            return QueryKind.Text;
        }
    }
}
