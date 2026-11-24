namespace PxeVice.Tests
{
    using System;
    using System.Collections.Generic;

    public static class OverviewTests
    {
        static readonly DateTime Now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

        public static void Run()
        {
            Data();
            Recent();
            Nav();
            Search();
        }

        static void Data()
        {
            DemoSite site = new DemoSite(Now);
            PxeLogStore store = new PxeLogStore();
            SiteData d = Engine.Read(site, store);
            OverviewData o = PxeOverview.From(d, Now, 24);
            T.Equal(d.Machines.Count, o.Machines, "the overview counts the machines");
            T.Equal(d.Machines.Count, o.NeedAttention, "every listed machine has something left behind");
            T.Check(o.Headline.Contains("need a look") || o.Headline.Contains("needs a look"), "the headline says what to look at");
            T.Equal(o.Boots, o.Served + o.NotServiced + OtherBoots(d, o), "served and not serviced never exceed the boots");
            bool seenGood = false;
            foreach (Machine m in o.Attention)
            {
                if (!m.NotServiced) seenGood = true;
                else T.Check(!seenGood, "machines that were not serviced come first");
            }
            int sum = 0;
            foreach (KeyValuePair<string, int> r in o.Reasons) sum += r.Value;
            T.Check(sum <= o.Machines && sum > 0, "the reasons count machines");
            for (int i = 1; i < o.Reasons.Count; i++) T.Check(o.Reasons[i - 1].Value >= o.Reasons[i].Value, "the most common reason is first");

            OverviewData none = PxeOverview.From(new SiteData(), Now, 24);
            T.Equal("Nothing needs a look", none.Headline, "an empty site needs nothing");
            T.Equal("In the last 24 hours, SMSPXE.log shows no PXE boot.", none.Lead, "no boots are said plainly");
            T.Equal("In the last 3 days", PxeOverview.From(null, Now, 72).Lead.Substring(0, 18), "72 hours are 3 days");
            T.Equal("In the last hour", PxeOverview.From(null, Now, 1).Lead.Substring(0, 16), "one hour is the last hour");
        }

        static int OtherBoots(SiteData d, OverviewData o)
        {
            int n = 0;
            DateTime since = Now.AddHours(-o.Hours);
            foreach (PxeRequest r in d.Requests)
            {
                if (!r.Utc.HasValue || r.Utc.Value < since || r.Utc.Value > Now.AddMinutes(5)) continue;
                if (!r.Served && r.Outcome != RequestOutcome.NotServiced && r.Outcome != RequestOutcome.NoDeployment) n++;
            }
            return n;
        }

        static void Recent()
        {
            MemorySettingStore s = new MemorySettingStore();
            T.Equal(0, RecentStore.Load(s).Count, "nothing is recent at first");
            RecentStore.Push(s, "k1", "PC-1");
            RecentStore.Push(s, "k2", "PC-2");
            RecentStore.Push(s, "k1", "PC-1");
            List<RecentMachine> l = RecentStore.Load(s);
            T.Equal(2, l.Count, "a machine opened again is not listed twice");
            T.Equal("k1", l[0].Key, "the newest is first");
            for (int i = 0; i < 20; i++) RecentStore.Push(s, "x" + i, "N" + i);
            T.Equal(RecentStore.Max, RecentStore.Load(s).Count, "only the last eight are kept");
            T.Equal("x19", RecentStore.Load(s)[0].Key, "the last one opened is first");
            RecentStore.Push(s, "a\tb\nc", "Na\tme");
            T.Equal("a b c", RecentStore.Load(s)[0].Key, "tabs and line breaks cannot break the list");
            RecentStore.Push(s, "", "x");
            RecentStore.Push(s, "y", "");
            T.Equal("a b c", RecentStore.Load(s)[0].Key, "an empty key or name is not added");
            RecentStore.Clear(s);
            T.Equal(0, RecentStore.Load(s).Count, "Clear empties the list");
        }

        static void Nav()
        {
            NavHistory h = new NavHistory();
            T.Check(!h.CanBack && !h.CanForward && h.Back() == null && h.Forward() == null, "nothing to go back to at first");
            h.Go(new NavEntry(0, null));
            h.Go(new NavEntry(1, "a"));
            h.Go(new NavEntry(1, "a"));
            T.Equal(2, h.Count, "the same place twice is one");
            h.Go(new NavEntry(1, "b"));
            T.Check(h.CanBack && !h.CanForward, "back, not forward");
            NavEntry e = h.Back();
            T.Equal("a", e.Key, "back goes to the place before");
            T.Check(h.CanForward, "forward is possible after back");
            h.Go(new NavEntry(2, null));
            T.Check(!h.CanForward, "a new place cuts off forward");
            T.Equal(3, h.Count, "and removes b");
            for (int i = 0; i < 80; i++) h.Go(new NavEntry(1, "m" + i));
            T.Equal(NavHistory.Max, h.Count, "the history is limited");
            T.Equal("m79", h.Current.Key, "the newest is current");
        }

        static void Search()
        {
            string n;
            T.Equal(QueryKind.Empty, MachineSearch.Kind("  ", out n), "blank is empty");
            T.Equal(QueryKind.Mac, MachineSearch.Kind("00-15-5d-23-21-3b", out n), "a MAC with dashes");
            T.Equal("00:15:5D:23:21:3B", n, "is normalized");
            T.Equal(QueryKind.Mac, MachineSearch.Kind("00155D23213B", out n), "a MAC without separators");
            T.Equal(QueryKind.Mac, MachineSearch.Kind("0015.5d23.213b", out n), "a MAC with dots");
            T.Equal(QueryKind.Guid, MachineSearch.Kind("{4c4c4544-0042-5710-8052-b4c04f4d3432}", out n), "a GUID with braces");
            T.Equal("4C4C4544-0042-5710-8052-B4C04F4D3432", n, "is upper case with dashes");
            T.Equal(QueryKind.Guid, MachineSearch.Kind("4C4C4544-0042-5710-8052-B4C04F4D3432", out n), "a GUID without braces");
            T.Equal(QueryKind.Text, MachineSearch.Kind("PC-017", out n), "a name is text");
            T.Equal(QueryKind.Text, MachineSearch.Kind("00:15:5D", out n), "a piece of a MAC is text, so it still matches by part");
            T.Equal(QueryKind.Text, MachineSearch.Kind("00000000-0000-0000-0000-000000000000", out n), "the all-zero GUID is not a GUID");
        }
    }
}
