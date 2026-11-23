namespace PxeVice.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    public static class UpdaterTests
    {
        const string Release =
            "{\"tag_name\":\"v0.4.0\",\"draft\":false,\"prerelease\":false," +
            "\"html_url\":\"https://github.com/Baldissera88/PxeVice/releases/tag/v0.4.0\"," +
            "\"assets\":[{\"size\":1,\"browser_download_url\":\"https://github.com/Baldissera88/PxeVice/releases/download/v0.4.0/PxeVice.msi\"}," +
            "{\"size\":2,\"browser_download_url\":\"https://github.com/Baldissera88/PxeVice/releases/download/v0.4.0/PxeVice.exe\"}," +
            "{\"size\":3,\"browser_download_url\":\"https://github.com/Baldissera88/PxeVice/releases/download/v0.4.0/SHA256SUMS.txt\"}," +
            "{\"size\":4,\"browser_download_url\":\"https://github.com/evil/other/releases/download/v0.4.0/PxeVice.exe\"}]," +
            "\"body\":\"## Notes\\r\\n\\r\\n- **One** thing\\r\\n- `Two` things\"}";

        public static void Run()
        {
            Dictionary<string, string> prefs = new Dictionary<string, string>();
            Updater.GetPref = delegate (string n) { string v; return prefs.TryGetValue(n, out v) ? v : null; };
            Updater.SetPref = delegate (string n, string v) { if (v == null) prefs.Remove(n); else prefs[n] = v; };
            bool pCheck = false, pInstall = false;
            Updater.Policy = delegate (string n) { return n == Updater.PolicyCheck ? pCheck : pInstall; };

            UpdateInfo r = new UpdateInfo();
            Updater.Parse(Release, "0.3.2", r);
            T.Equal("0.4.0", r.Latest, "latest version without v");
            T.Check(r.Newer, "0.4.0 is newer than 0.3.2");
            T.Check(r.MsiUrl != null && r.MsiUrl.EndsWith("v0.4.0/PxeVice.msi"), "msi link");
            T.Check(r.ExeUrl != null && r.ExeUrl.Contains("Baldissera88/PxeVice"), "exe link is from this repository only");
            T.Check(r.SumsUrl != null, "sums link");
            T.Equal("Notes\n\n\u2022 One thing\n\u2022 Two things", r.Notes, "notes as plain lines");

            UpdateInfo same = new UpdateInfo();
            Updater.Parse(Release, "0.4.0", same);
            T.Check(!same.Newer, "same version is not newer");
            T.Check(Updater.IsNewer("0.10.0", "0.9.9"), "numbers, not text");
            T.Check(!Updater.IsNewer("0.3.2", "0.4.0"), "older is not newer");
            T.Check(!Updater.IsNewer("junk", "0.4.0"), "junk is not newer");

            UpdateInfo pre = new UpdateInfo();
            Updater.Parse(Release.Replace("v0.4.0\",\"draft", "v0.5.0-beta\",\"draft"), "0.3.2", pre);
            T.Check(pre.Prerelease && !pre.Newer, "a pre-release is not offered");

            string a = new string('a', 64), b = new string('B', 64);
            string sums = a + "  PxeVice.msi\n" + b + " *PxeVice.exe\n";
            T.Equal(a, Updater.ExpectedHash(sums, "PxeVice.msi"), "hash of the msi");
            T.Equal(new string('b', 64), Updater.ExpectedHash(sums, "PxeVice.exe"), "hash of the exe, lower case, binary marker");
            T.Equal(null, Updater.ExpectedHash(sums, "Other.exe"), "no hash for a file not listed");

            string dir = Path.Combine(Path.GetTempPath(), "pxevice-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string f = Path.Combine(dir, "PxeVice.exe");
                File.WriteAllText(f, "hello");
                string hash = Updater.FileHash(f);
                T.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", hash, "SHA-256 of a file");
                T.Check(Updater.Matches(hash + "  PxeVice.exe\n", "PxeVice.exe", f), "a file with the listed hash matches");
                T.Check(!Updater.Matches(a + "  PxeVice.exe\n", "PxeVice.exe", f), "a file with another hash does not match");
                T.Equal("0.4.0", Updater.FolderVersion(Path.Combine(Path.Combine(dir, "PxeVice-update-0.4.0"), "PxeVice.msi")), "version from the work folder");
                string w = Updater.NewWorkFolder(dir, "0.4.0/../x");
                T.Check(Directory.Exists(w) && Path.GetDirectoryName(w) == dir, "work folder stays under the root");
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }

            string args = Updater.HelperArgs(@"C:\u\PxeVice-update-0.4.0\PxeVice.msi", @"C:\Program Files\PxeVice\PxeVice.exe", 42);
            T.Equal("--apply-update msi \"C:\\u\\PxeVice-update-0.4.0\\PxeVice.msi\" \"C:\\Program Files\\PxeVice\\PxeVice.exe\" 42", args, "helper arguments");
            T.Check(UpdateJob.Is(new string[] { "--apply-update", "exe", "a", "b", "1" }), "helper arguments are recognised");
            T.Check(!UpdateJob.Is(new string[] { "/list" }), "other arguments are not");

            // The check.
            int fetched = 0;
            Updater.Fetch = delegate (string url) { fetched++; return Release; };
            UpdateInfo c = Updater.Check();
            T.Equal(1, fetched, "check asks GitHub");
            T.Check(c.Error == null && c.CheckedUtc != default(DateTime), "check has no error");
            T.Check(!Updater.DueAt(DateTime.UtcNow.AddHours(1)), "no second check within the day");
            T.Check(Updater.DueAt(DateTime.UtcNow.AddHours(21)), "a check is due after 20 hours");

            Updater.CheckOn = false;
            fetched = 0;
            UpdateInfo off = Updater.Check();
            T.Check(fetched == 0 && off.Error != null, "turned off in Settings: GitHub is not asked");
            Updater.CheckOn = true;
            pCheck = true;
            off = Updater.Check();
            T.Check(fetched == 0 && off.Error != null && off.Error.Contains(Updater.PolicyCheck), "turned off by policy: GitHub is not asked");
            pCheck = false;

            Updater.Fetch = delegate (string url) { throw new System.Net.WebException("offline"); };
            T.Equal("GitHub could not be reached.", Updater.Check().Error, "offline says so");

            // Installing is off until it is turned on, and the policy turns it off for everyone.
            T.Check(Updater.InstallBlocked != null, "install is off by default");
            T.Check(!Updater.CanInstall(r, true), "cannot install while it is off");
            Updater.InstallOn = true;
            T.Check(Updater.InstallBlocked == null, "install can be turned on");
            T.Check(Updater.CanInstall(r, true) && Updater.CanInstall(r, false), "installable from the msi and the exe");
            pInstall = true;
            T.Check(Updater.InstallBlocked != null && Updater.InstallBlocked.Contains(Updater.PolicyInstall), "policy turns install off");
            T.Check(!Updater.CanInstall(r, true), "cannot install by policy");
            pInstall = false;
            UpdateInfo nosums = new UpdateInfo();
            Updater.Parse(Release.Replace("SHA256SUMS.txt", "Other.txt"), "0.3.2", nosums);
            T.Check(!Updater.CanInstall(nosums, true), "no checksum file, no automatic install");
        }
    }
}
