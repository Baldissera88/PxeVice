namespace PxeVice.Tests
{
    using System;
    using System.Collections.Generic;

    public static class SettingsTests
    {
        public static void Run()
        {
            Catalog();
            Changed();
            Search();
        }

        static void Catalog()
        {
            HashSet<string> ids = new HashSet<string>();
            foreach (SettingDef d in SettingsCatalog.All)
            {
                T.Check(ids.Add(d.Id), "setting id is unique: " + d.Id);
                T.Check(SettingsCatalog.Page(d.Page) != null, "setting " + d.Id + " is on a known page");
                T.Check(!string.IsNullOrEmpty(d.Summary), "setting " + d.Id + " has a description");
            }
            foreach (SettingPage p in SettingsCatalog.Pages)
                T.Check(SettingsCatalog.On(p.Id).Count > 0, "page " + p.Title + " has settings");
            T.Equal(SettingsCatalog.General, SettingsCatalog.Remembered("nonsense"), "an unknown saved page opens General");
            T.Equal(SettingsCatalog.Watch, SettingsCatalog.Remembered("watch"), "a saved page is remembered");
            // Every old setting is still in the catalog, with the default the program uses.
            string[] keys = { "Theme", "WatchOnStart", "SiteServer", "SqlServer", "Database", "FindPxeServers", "PxeServers", "PxeLogPath", "WatchSeconds", "WatchAfterReset", "RequestHours", "DeleteUnknown", "RestartPxe", "UpdateCheck", "UpdateInstall", "LogVicePath" };
            foreach (string k in keys)
            {
                bool found = false;
                foreach (SettingDef d in SettingsCatalog.All) if (Array.IndexOf(d.Keys, k) >= 0) found = true;
                T.Check(found, "the value " + k + " is a setting");
            }
        }

        static void Changed()
        {
            MemorySettingStore s = new MemorySettingStore();
            SettingDef wd = SettingsCatalog.Find("watchseconds");
            T.Check(!wd.Changed(s), "nothing saved is the default");
            s.Set("WatchSeconds", "10");
            T.Check(!wd.Changed(s), "the default saved is not a change");
            s.Set("WatchSeconds", "30");
            T.Check(wd.Changed(s), "another value is a change");
            SettingDef rs = SettingsCatalog.Find("restartpxe");
            s.Set("RestartPxe", "true");
            T.Check(rs.Changed(s), "true counts as on, which is not the default off");
            s.Set("RestartPxe", "0");
            T.Check(!rs.Changed(s), "0 is the default");
            s.Set("DeleteUnknown", "0");
            s.Set("RestartPxe", "1");
            T.Equal(3, SettingsCatalog.ChangedOn(SettingsCatalog.Watch, s).Count + SettingsCatalog.ChangedOn(SettingsCatalog.Resets, s).Count, "a changed value on each page is counted");
            int n = SettingsCatalog.Reset(SettingsCatalog.Resets, s);
            T.Equal(2, n, "Reset removes both saved values of the page");
            T.Check(s.Get("DeleteUnknown") == null && s.Get("RestartPxe") == null, "the defaults apply again");
            T.Equal("30", s.Get("WatchSeconds"), "other pages are left alone");
            T.Check(!SettingsCatalog.Find("account").Resettable, "the account has no default, so it is never reset");
            T.Check(!SettingsCatalog.Resettable(SettingsCatalog.Account), "the account page has nothing to reset");
        }

        static void Search()
        {
            T.Check(SettingsCatalog.Search("").Count == 0, "an empty search finds nothing");
            List<SettingDef> hits = SettingsCatalog.Search("theme");
            T.Check(hits.Count > 0 && hits[0].Id == "theme", "the name comes first");
            hits = SettingsCatalog.Search("cm_ login");
            T.Check(hits.Count > 0 && hits[0].Id == "database", "the database help is found from the words of the error");
            hits = SettingsCatalog.Search("password");
            T.Check(hits.Count > 0, "the password is found in the keywords");
            T.Check(SettingsCatalog.Search("zzzzqq").Count == 0, "no match");
        }
    }
}
