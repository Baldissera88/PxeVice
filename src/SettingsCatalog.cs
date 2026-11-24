namespace PxeVice
{
    using System;
    using System.Collections.Generic;

    // Where settings are kept, so tests can use memory instead of HKCU\Software\PxeVice.
    public interface ISettingStore
    {
        string Get(string name);
        // A null value removes the setting, so its default applies again.
        void Set(string name, string value);
    }

    public sealed class MemorySettingStore : ISettingStore
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Get(string name)
        {
            string v;
            return Values.TryGetValue(name, out v) ? v : null;
        }

        public void Set(string name, string value)
        {
            if (value == null) Values.Remove(name);
            else Values[name] = value;
        }
    }

    // One subject of the Settings window.
    public sealed class SettingPage
    {
        public string Id, Title, Summary;
    }

    // One setting as Settings shows and finds it.
    public sealed class SettingDef
    {
        public string Id, Page, Title, Summary, More, Keywords;
        // The title of the row when another part of PxeVice makes the row, such as Your storage or Updates.
        public string Row;
        // The values under HKCU\Software\PxeVice and what each is when nothing is saved. Only settings with
        // defaults get the changed mark and are reset. Customers, sign-ins and Your storage have none.
        public string[] Keys = new string[0];
        public string[] Defaults = new string[0];

        public bool Resettable
        {
            get { return Keys.Length > 0 && Defaults.Length == Keys.Length; }
        }

        public bool Changed(ISettingStore s)
        {
            if (!Resettable || s == null) return false;
            for (int i = 0; i < Keys.Length; i++)
            {
                if (!Same(s.Get(Keys[i]), Defaults[i])) return true;
            }
            return false;
        }

        // Removes the saved values, so the defaults apply again.
        public int Reset(ISettingStore s)
        {
            if (!Resettable || s == null) return 0;
            int n = 0;
            foreach (string k in Keys)
            {
                if (s.Get(k) == null) continue;
                s.Set(k, null);
                n++;
            }
            return n;
        }

        // Nothing saved is the default. Yes and no are kept as 1 and 0, and some older values as true and false.
        static bool Same(string value, string fallback)
        {
            if (value == null) return true;
            string v = value.Trim();
            if (fallback == "1" || fallback == "0")
            {
                bool on = v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                bool off = v == "0" || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
                if (on || off) return on == (fallback == "1");
            }
            return string.Equals(v, fallback, StringComparison.OrdinalIgnoreCase);
        }
    }

    // The subjects of the Settings window and every setting on them, in the order they are shown.
    public static class SettingsCatalog
    {
        public const string General = "general", Site = "site", Account = "account", Watch = "watch", Resets = "reset",
            Updates = "updates", LogVice = "logvice", Data = "data";

        // The subject Settings opened on last.
        public const string LastPagePref = "SettingsPage";

        public static readonly SettingPage[] Pages =
        {
            P(General, "General", "The theme and what PxeVice does when it opens."),
            P(Site, "Site", "The site server, the site database and the PXE distribution points PxeVice reads."),
            P(Account, "Service account", "The account PxeVice connects to the site with."),
            P(Watch, "Watch and PXE boots", "How often PxeVice reads, and how far back it looks for PXE boots."),
            P(Resets, "Reset", "What the reset page has turned on when it opens."),
            P(Updates, "Updates", "The version you have and the newest on GitHub."),
            P(LogVice, "LogVice", "Where LogVice is, for the Open in LogVice button."),
            P(Data, "Data and privacy", "What PxeVice keeps on this PC and what it sends."),
        };

        public static readonly SettingDef[] All =
        {
            D("theme", General, "Theme", "The colours of PxeVice.",
                "Vice is the default. System follows the Windows app mode. Terminal and Amber have square corners and show numbers in a monospace font. The same themes as TrackVice.",
                "appearance colour color dark light vice terminal amber synthwave", "Theme", Palettes.Default),
            D("watchonstart", Watch, "Watch when PxeVice opens", "Start watching at once.", null, "start open follow", "WatchOnStart", "0"),

            D("siteserver", Site, "Site server", "The server with the SMS Provider. Empty means this computer.", null, "provider sms wmi connect", "SiteServer", null),
            D("sqlserver", Site, "SQL Server", "Found from the site when empty. For a named instance, use SERVER\\INSTANCE.", null, "database instance sql", "SqlServer", null),
            D("database", Site, "Site database", "Found from the site when empty, such as CM_P01.", "If you see \u201cCannot open database P01\u201d, the name is the site code. The database is called CM_ and the site code.", "cm_ login failed cannot open", "Database", null),
            D("findpxe", Site, "Find the PXE distribution points from the site", "Every distribution point with PXE turned on, from the site.", null, "dp distribution point pxe servers", "FindPxeServers", "1"),
            D("pxeservers", Site, "PXE distribution points", "Added to the ones found, or the only ones used when the setting above is off. Separate several with commas.", null, "dp servers", "PxeServers", null),
            D("pxelog", Site, "SMSPXE.log", "Only when PxeVice does not find it. <server> stands for each PXE distribution point.", "Such as \\\\<server>\\D$\\SMS\\Logs\\SMSPXE.log.", "log path share", "PxeLogPath", null),
            D("savedsites", Site, "Saved sites", "Sites you switch between with Site in the toolbar.", null, "switch lab production"),

            D("account", Account, "Account", "PxeVice connects to the site, the database and the PXE points with this account, like runas /netonly.", "On this PC you are still yourself, and the activity log says both. Leave it empty to use your own account.", "service account runas netonly password dpapi svc", RunAs.UserPref, null),
            D("useaccount", Account, "Use the account", "Connects again with the account now.", null, "connect password forget"),

            D("watchseconds", Watch, "Read every", "How often Watch reads the site and the new lines of SMSPXE.log.", null, "interval seconds refresh", "WatchSeconds", "10"),
            D("watchafterreset", Watch, "Watch after a reset", "Start watching after a reset, so PxeVice tells what the machine gets at its next PXE boot.", null, "follow", "WatchAfterReset", "1"),
            D("requesthours", Watch, "PXE boots from the last", "The PXE boots tab, and machines listed only for a PXE boot that was not serviced.", "PxeVice reads up to 8 MB of SMSPXE.log and SMSPXE.lo_ when it connects.", "hours days history log", "RequestHours", "24"),

            D("deleteunknown", Resets, "Delete Unknown device records", "Turned on when the reset page opens.", "Named device records are never deleted unless you turn that on for the reset.", "unknown computer record x86 x64", "DeleteUnknown", "1"),
            D("restartpxe", Resets, "Restart the PXE service", "Turned on when the reset page opens.", "Needs administrator rights on the distribution point.", "wds smspxe service", "RestartPxe", "0"),

            D("updatecheck", Updates, "Check for updates", "Once a day when PxeVice opens, it asks GitHub for the latest release.", "Nothing about you or the site is sent. Administrators can turn it off for everyone with the DWORD DisableUpdateCheck = 1 under HKLM\\SOFTWARE\\Policies\\PxeVice.", "github version release", "UpdateCheck", "1"),
            D("updateinstall", Updates, "Install updates from PxeVice", "Shows an Install button for a new version.", "PxeVice downloads the MSI (or the exe when it is not installed), checks it against SHA256SUMS.txt, closes, installs and opens again. The MSI asks for administrator rights. The DWORD DisableUpdateInstall = 1 under the same key turns it off for everyone.", "github download msi", "UpdateInstall", "0"),
            D("version", Updates, "Version", "The version you have and when PxeVice last asked GitHub.", null, "check now about"),

            D("logvice", LogVice, "LogVice", "Where LogVice is.", null, "log viewer browse locate", "LogVicePath", null),

            D("settingsfile", Data, "Where settings are stored", "In the registry, for your Windows user only.", null, "registry hkcu"),
            D("activitylog", Data, "Activity log", "Every change PxeVice makes, with who made it, in the CMTrace format.", null, "cmtrace history audit folder"),
        };

        static SettingPage P(string id, string title, string summary)
        {
            SettingPage p = new SettingPage();
            p.Id = id;
            p.Title = title;
            p.Summary = summary;
            return p;
        }

        // keyDefault holds pairs of a key and its default. A null default means the value has none, so it is not reset.
        static SettingDef D(string id, string page, string title, string summary, string more, string keywords, params string[] keyDefault)
        {
            SettingDef d = new SettingDef();
            d.Id = id;
            d.Page = page;
            d.Title = title;
            d.Summary = summary;
            d.More = more;
            d.Keywords = keywords;
            List<string> keys = new List<string>(), defaults = new List<string>();
            for (int i = 0; i + 1 < keyDefault.Length; i += 2)
            {
                keys.Add(keyDefault[i]);
                if (keyDefault[i + 1] != null) defaults.Add(keyDefault[i + 1]);
            }
            d.Keys = keys.ToArray();
            d.Defaults = defaults.ToArray();
            return d;
        }

        static SettingDef Row(SettingDef d, string row)
        {
            d.Row = row;
            return d;
        }

        public static SettingPage Page(string id)
        {
            foreach (SettingPage p in Pages) if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        public static SettingDef Find(string id)
        {
            foreach (SettingDef d in All) if (d.Id == id) return d;
            return null;
        }

        public static List<SettingDef> On(string page)
        {
            List<SettingDef> l = new List<SettingDef>();
            foreach (SettingDef d in All) if (d.Page == page) l.Add(d);
            return l;
        }

        // The saved subject, or General when none is saved or it is not known any more.
        public static string Remembered(string stored)
        {
            SettingPage p = Page(stored);
            return p != null ? p.Id : General;
        }

        public static bool Resettable(string page)
        {
            foreach (SettingDef d in On(page)) if (d.Resettable) return true;
            return false;
        }

        public static List<SettingDef> ChangedOn(string page, ISettingStore s)
        {
            List<SettingDef> l = new List<SettingDef>();
            foreach (SettingDef d in On(page)) if (d.Changed(s)) l.Add(d);
            return l;
        }

        // Puts back the defaults of one subject. Returns how many saved values were removed.
        public static int Reset(string page, ISettingStore s)
        {
            int n = 0;
            foreach (SettingDef d in On(page)) n += d.Reset(s);
            return n;
        }

        // Settings whose name, description or subject has every word of the text. Names that match come first.
        public static List<SettingDef> Search(string text)
        {
            List<SettingDef> hits = new List<SettingDef>();
            string q = (text ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0) return hits;
            string[] words = q.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            List<int> ranks = new List<int>();
            foreach (SettingDef d in All)
            {
                string title = d.Title.ToLowerInvariant();
                SettingPage p = Page(d.Page);
                string all = title + " " + (d.Summary ?? "") + " " + (d.More ?? "") + " " + (d.Keywords ?? "") + " " + (p != null ? p.Title : "");
                all = all.ToLowerInvariant();
                bool every = true, inTitle = true;
                foreach (string w in words)
                {
                    if (all.IndexOf(w, StringComparison.Ordinal) < 0) every = false;
                    if (title.IndexOf(w, StringComparison.Ordinal) < 0) inTitle = false;
                }
                if (!every) continue;
                int rank = title == q ? 0 : title.StartsWith(q, StringComparison.Ordinal) ? 1 : inTitle ? 2 : 3;
                int at = 0;
                while (at < hits.Count && ranks[at] <= rank) at++;
                hits.Insert(at, d);
                ranks.Insert(at, rank);
            }
            return hits;
        }
    }
}
