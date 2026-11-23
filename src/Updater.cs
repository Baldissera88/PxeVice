namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using Microsoft.Win32;

    // What GitHub says about the latest release of PxeVice.
    public sealed class UpdateInfo
    {
        public string Latest;
        public string Url;
        public bool Newer;
        public bool Prerelease;
        public string Error;
        public string MsiUrl;
        public string ExeUrl;
        public string SumsUrl;
        // The SHA-256 GitHub gives for a file of the release, in lower case, when it does. Used when the release has no SHA256SUMS.txt.
        public string MsiDigest, ExeDigest;
        public string Notes;
        public DateTime CheckedUtc;
    }

    // Checks the GitHub releases of PxeVice for a newer version, and can install it.
    // The check is on by default and can be turned off in Settings, or for everyone with the policy value DisableUpdateCheck.
    // Installing is off until it is turned on in Settings, and the policy value DisableUpdateInstall turns it off for everyone.
    public static class Updater
    {
        public const string Repo = "Baldissera88/PxeVice";
        public const string ReleasesUrl = "https://github.com/" + Repo + "/releases";
        public const string ApiUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const string MsiName = "PxeVice.msi";
        public const string ExeName = "PxeVice.exe";
        public const string SumsName = "SHA256SUMS.txt";
        public const string PolicyKey = @"SOFTWARE\Policies\PxeVice";
        public const string PolicyCheck = "DisableUpdateCheck";
        public const string PolicyInstall = "DisableUpdateInstall";
        public const string FolderPrefix = "PxeVice-update-";

        // Where the settings are kept. Replaced in the tests.
        public static Func<string, string> GetPref = delegate (string name) { return Prefs.Get(name, null); };
        public static Action<string, string> SetPref = delegate (string name, string value) { Prefs.Set(name, value); };
        // Reads a policy value of this PC, 1 when it is on. Replaced in the tests.
        public static Func<string, bool> Policy = ReadPolicy;
        // Reads a URL as text. Replaced in the tests.
        public static Func<string, string> Fetch = FetchText;

        // The result of the last check in this run, or null.
        public static UpdateInfo Last;

        public static string Current
        {
            get { return Cli.Version; }
        }

        static bool ReadPolicy(string name)
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = b.OpenSubKey(PolicyKey))
                {
                    if (k == null) return false;
                    long n;
                    return long.TryParse(Convert.ToString(k.GetValue(name), CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n != 0;
                }
            }
            catch (Exception) { return false; }
        }

        // Null when PxeVice may ask GitHub, otherwise why not.
        public static string CheckBlocked
        {
            get
            {
                if (Policy(PolicyCheck)) return "Your organization has turned off the update check with the policy value " + PolicyCheck + ".";
                if (GetPref("UpdateCheck") == "0") return "The update check is turned off in Settings.";
                return null;
            }
        }

        // Null when PxeVice may install an update, otherwise why not.
        public static string InstallBlocked
        {
            get
            {
                if (Policy(PolicyCheck)) return "Your organization has turned off the update check with the policy value " + PolicyCheck + ".";
                if (Policy(PolicyInstall)) return "Your organization has turned off installing updates with the policy value " + PolicyInstall + ".";
                if (GetPref("UpdateInstall") != "1") return "Installing updates from PxeVice is turned off in Settings.";
                return null;
            }
        }

        public static bool CheckOn
        {
            get { return GetPref("UpdateCheck") != "0"; }
            set { SetPref("UpdateCheck", value ? null : "0"); }
        }

        public static bool InstallOn
        {
            get { return GetPref("UpdateInstall") == "1"; }
            set { SetPref("UpdateInstall", value ? "1" : null); }
        }

        public static DateTime? LastChecked
        {
            get
            {
                long ticks;
                string s = GetPref("UpdateChecked");
                if (string.IsNullOrEmpty(s) || !long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks)) return null;
                try { return new DateTime(ticks, DateTimeKind.Utc); }
                catch (ArgumentException) { return null; }
            }
        }

        // At most once a day. 20 hours, so a start at about the same time each day still checks.
        public static bool DueAt(DateTime nowUtc)
        {
            DateTime? last = LastChecked;
            return last == null || last.Value > nowUtc || nowUtc - last.Value > TimeSpan.FromHours(20);
        }

        public static void CheckAsync(Action<UpdateInfo> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo r = Check();
                if (done != null) done(r);
            });
        }

        public static UpdateInfo Check()
        {
            UpdateInfo r = new UpdateInfo();
            string blocked = CheckBlocked;
            if (blocked != null) { r.Error = blocked; return r; }
            try
            {
                Parse(Fetch(ApiUrl), Current, r);
                r.CheckedUtc = DateTime.UtcNow;
                SetPref("UpdateChecked", r.CheckedUtc.Ticks.ToString(CultureInfo.InvariantCulture));
                Last = r;
            }
            catch (WebException ex)
            {
                HttpWebResponse hr = ex.Response as HttpWebResponse;
                r.Error = hr != null && hr.StatusCode == HttpStatusCode.NotFound ? "No release has been published yet." : "GitHub could not be reached.";
            }
            catch (Exception) { r.Error = "GitHub could not be reached."; }
            return r;
        }

        static readonly Regex RxTag = new Regex("\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
        static readonly Regex RxUrl = new Regex("\"html_url\"\\s*:\\s*\"(https://github\\.com/" + Regex.Escape(Repo) + "/releases/tag/[^\"]+)\"", RegexOptions.IgnoreCase);
        static readonly Regex RxPre = new Regex("\"prerelease\"\\s*:\\s*true");
        static readonly Regex RxDraft = new Regex("\"draft\"\\s*:\\s*true");
        static readonly Regex RxDigest = new Regex("\"digest\"\\s*:\\s*\"sha256:([0-9a-fA-F]{64})\"");
        static readonly Regex RxBody = new Regex("\"body\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");

        // Reads a reply of releases/latest. Only files of this repository's own releases are taken.
        public static void Parse(string json, string current, UpdateInfo r)
        {
            json = json ?? "";
            Match tag = RxTag.Match(json);
            if (!tag.Success) { r.Error = "GitHub returned no version."; return; }
            r.Latest = tag.Groups[1].Value.Trim().TrimStart('v', 'V');
            Match url = RxUrl.Match(json);
            r.Url = url.Success ? url.Groups[1].Value : ReleasesUrl;
            r.Prerelease = RxPre.IsMatch(json) || RxDraft.IsMatch(json) || r.Latest.IndexOf('-') >= 0;
            r.Newer = !r.Prerelease && IsNewer(r.Latest, current);
            Regex files = new Regex("\"browser_download_url\"\\s*:\\s*\"(https://github\\.com/" + Regex.Escape(Repo) + "/releases/download/[^\"/]+/(" + Regex.Escape(MsiName) + "|" + Regex.Escape(ExeName) + "|" + Regex.Escape(SumsName) + "))\"", RegexOptions.IgnoreCase);
            int after = 0;
            foreach (Match m in files.Matches(json))
            {
                string name = m.Groups[2].Value;
                string digest = null;
                foreach (Match d in RxDigest.Matches(json.Substring(after, m.Index - after))) digest = d.Groups[1].Value.ToLowerInvariant();
                after = m.Index + m.Length;
                if (string.Equals(name, MsiName, StringComparison.OrdinalIgnoreCase) && r.MsiDigest == null) r.MsiDigest = digest;
                else if (string.Equals(name, ExeName, StringComparison.OrdinalIgnoreCase) && r.ExeDigest == null) r.ExeDigest = digest;
                if (string.Equals(name, MsiName, StringComparison.OrdinalIgnoreCase) && r.MsiUrl == null) r.MsiUrl = m.Groups[1].Value;
                else if (string.Equals(name, ExeName, StringComparison.OrdinalIgnoreCase) && r.ExeUrl == null) r.ExeUrl = m.Groups[1].Value;
                else if (string.Equals(name, SumsName, StringComparison.OrdinalIgnoreCase) && r.SumsUrl == null) r.SumsUrl = m.Groups[1].Value;
            }
            Match body = RxBody.Match(json);
            if (body.Success) r.Notes = NotesText(JsonString(body.Groups[1].Value));
        }

        static bool TryVersion(string s, out int[] parts)
        {
            parts = new int[3];
            if (string.IsNullOrEmpty(s)) return false;
            string[] p = s.Trim().Split('.');
            if (p.Length < 3 || p.Length > 4) return false;
            for (int i = 0; i < 3; i++)
                if (!int.TryParse(p[i], NumberStyles.None, CultureInfo.InvariantCulture, out parts[i])) return false;
            return true;
        }

        public static bool IsNewer(string latest, string current)
        {
            int[] a, b;
            if (!TryVersion(latest, out a) || !TryVersion(current, out b)) return false;
            for (int i = 0; i < 3; i++)
                if (a[i] != b[i]) return a[i] > b[i];
            return false;
        }

        public static string JsonString(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                int code;
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < s.Length && int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        else sb.Append(n);
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        // The release notes as plain lines.
        public static string NotesText(string md)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string raw in (md ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("|")) continue;
                line = line.Replace("**", "").Replace("`", "");
                if (line.StartsWith("#")) line = line.TrimStart('#').Trim();
                if (line.StartsWith("- ") || line.StartsWith("* ")) line = "• " + line.Substring(2);
                if (line.Length == 0 && (sb.Length == 0 || sb.ToString().EndsWith("\n\n"))) continue;
                sb.Append(line).Append('\n');
            }
            return sb.ToString().Trim();
        }

        static HttpWebRequest Request(string url, int timeout)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "PxeVice/" + Current;
            req.Timeout = timeout;
            req.ReadWriteTimeout = timeout;
            req.AllowAutoRedirect = true;
            IWebProxy proxy = WebRequest.GetSystemWebProxy();
            if (proxy != null) proxy.Credentials = CredentialCache.DefaultNetworkCredentials;
            req.Proxy = proxy;
            return req;
        }

        static string FetchText(string url)
        {
            HttpWebRequest req = Request(url, 10000);
            req.Accept = "application/vnd.github+json";
            using (WebResponse resp = req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        const long MaxDownload = 128L * 1024 * 1024;

        static void Download(string url, string path, Action<int> progress)
        {
            HttpWebRequest req = Request(url, 60000);
            bool ok = false;
            try
            {
                using (WebResponse resp = req.GetResponse())
                using (Stream src = resp.GetResponseStream())
                using (FileStream dst = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    long length = resp.ContentLength > 0 ? resp.ContentLength : 0;
                    byte[] buf = new byte[81920];
                    int n, last = -1;
                    long total = 0;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n);
                        total += n;
                        if (total > MaxDownload) throw new IOException("The download is larger than expected.");
                        if (progress != null && length > 0)
                        {
                            int pct = (int)Math.Min(100, total * 100 / length);
                            if (pct != last) { last = pct; progress(pct); }
                        }
                    }
                }
                ok = true;
            }
            finally
            {
                if (!ok) try { File.Delete(path); } catch (Exception) { }
            }
        }

        // The hash SHA256SUMS.txt gives for file, in lower case, or null. Lines are "<hash>  <file>" as sha256sum writes them.
        public static string ExpectedHash(string sums, string file)
        {
            foreach (string raw in (sums ?? "").Split('\n'))
            {
                string line = raw.Trim();
                int sp = line.IndexOf(' ');
                if (sp != 64 || !IsHex(line.Substring(0, 64))) continue;
                string name = line.Substring(sp).Trim().TrimStart('*');
                if (string.Equals(name, file, StringComparison.OrdinalIgnoreCase)) return line.Substring(0, 64).ToLowerInvariant();
            }
            return null;
        }

        static bool IsHex(string s)
        {
            foreach (char c in s)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            return true;
        }

        public static string FileHash(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(64);
                foreach (byte b in h) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        // True when the file has the hash SHA256SUMS.txt gives for name.
        public static bool Matches(string sums, string name, string file)
        {
            string want = ExpectedHash(sums, name);
            return want != null && File.Exists(file) && FileHash(file) == want;
        }

        // A folder only this user can write: under %LOCALAPPDATA%, made new for each download.
        public static string UpdatesRoot
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local)) local = Path.GetTempPath();
                return Path.Combine(Path.Combine(local, "PxeVice"), "Updates");
            }
        }

        public static string NewWorkFolder(string root, string version)
        {
            string safe = Regex.Replace(version ?? "", "[^0-9A-Za-z.\\-]", "");
            string dir = Path.Combine(root, FolderPrefix + safe);
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); }
                catch (Exception) { dir = Path.Combine(root, FolderPrefix + safe + "-" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)); }
            }
            Directory.CreateDirectory(dir);
            return dir;
        }

        // The version in a work folder's name, or null.
        public static string FolderVersion(string file)
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(file ?? "") ?? "") ?? "";
            if (!folder.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase)) return null;
            string v = folder.Substring(FolderPrefix.Length);
            int dash = v.IndexOf('-');
            return dash > 0 && v.Length - dash > 12 ? v.Substring(0, dash) : v;
        }

        // True when this copy was installed with the MSI, which writes InstallDir.
        public static bool IsInstalled(string exePath)
        {
            try
            {
                string dir;
                using (RegistryKey b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = b.OpenSubKey(@"SOFTWARE\PxeVice"))
                    dir = k != null ? k.GetValue("InstallDir") as string : null;
                return SameFolder(dir, Path.GetDirectoryName(exePath));
            }
            catch (Exception) { return false; }
        }

        public static bool SameFolder(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        // Why this release cannot be installed from PxeVice, or null when it can.
        public static string WhyNotInstall(UpdateInfo r, bool installed)
        {
            if (r == null || !r.Newer) return "There is no newer version.";
            string blocked = InstallBlocked;
            if (blocked != null) return blocked;
            string name = installed ? MsiName : ExeName;
            if ((installed ? r.MsiUrl : r.ExeUrl) == null) return "This release has no " + name + ", so PxeVice cannot install it.";
            if (r.SumsUrl == null && (installed ? r.MsiDigest : r.ExeDigest) == null) return "This release has no " + SumsName + ", and GitHub gives no checksum for " + name + ", so PxeVice cannot check it before installing.";
            return null;
        }

        public static bool CanInstall(UpdateInfo r, bool installed)
        {
            return WhyNotInstall(r, installed) == null;
        }

        // Downloads SHA256SUMS.txt and the MSI or exe to a new folder and checks the hash. Returns the checked file.
        public static string Prepare(UpdateInfo r, bool installed, Action<int> progress)
        {
            string blocked = InstallBlocked;
            if (blocked != null) throw new InvalidOperationException(blocked);
            string url = installed ? r.MsiUrl : r.ExeUrl;
            string name = installed ? MsiName : ExeName;
            string why = WhyNotInstall(r, installed);
            if (why != null) throw new InvalidOperationException(why + " Download it from the release page.");
            string dir = NewWorkFolder(UpdatesRoot, r.Latest);
            string file = Path.Combine(dir, name);
            // SHA256SUMS.txt from the release first; the checksum GitHub gives for the file when the release has none.
            string want = null;
            if (r.SumsUrl != null)
            {
                string sumsPath = Path.Combine(dir, SumsName);
                Download(r.SumsUrl, sumsPath, null);
                want = ExpectedHash(File.ReadAllText(sumsPath), name);
            }
            if (want == null) want = installed ? r.MsiDigest : r.ExeDigest;
            if (want == null) throw new InvalidOperationException(SumsName + " in the release does not list " + name + ". Download it from the release page.");
            Download(url, file, progress);
            if (!File.Exists(file) || FileHash(file) != want.ToLowerInvariant())
            {
                try { File.Delete(file); } catch (Exception) { }
                throw new InvalidOperationException("The downloaded " + name + " does not match its checksum, so it was not installed. Try again later, or download it from the release page.");
            }
            return file;
        }

        // The arguments for the helper: --apply-update msi|exe "<file>" "<target>" <pid>.
        public static string HelperArgs(string file, string target, int pid)
        {
            bool msi = file.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
            return "--apply-update " + (msi ? "msi" : "exe") + " \"" + file + "\" \"" + target + "\" " + pid.ToString(CultureInfo.InvariantCulture);
        }

        // Starts a copy of this exe from the work folder as the helper. It closes PxeVice, installs and starts PxeVice again.
        public static void Launch(string file, string exePath)
        {
            string dir = Path.GetDirectoryName(file);
            string helper = Path.Combine(dir, "PxeVice-updater.exe");
            File.Copy(exePath, helper, true);
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(helper, HelperArgs(file, exePath, System.Diagnostics.Process.GetCurrentProcess().Id));
            psi.UseShellExecute = false;
            psi.WorkingDirectory = dir;
            System.Diagnostics.Process.Start(psi);
        }

        // Removes work folders of earlier updates, but not the one this copy runs from, nor one just made.
        public static void CleanUp(string exePath)
        {
            try
            {
                string root = UpdatesRoot;
                if (!Directory.Exists(root)) return;
                string mine = Path.GetDirectoryName(exePath) ?? "";
                foreach (string d in Directory.GetDirectories(root, FolderPrefix + "*"))
                {
                    if (mine.StartsWith(d, StringComparison.OrdinalIgnoreCase)) continue;
                    if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(d) < TimeSpan.FromMinutes(10)) continue;
                    try { Directory.Delete(d, true); } catch (Exception) { }
                }
            }
            catch (Exception) { }
        }
    }

    // What the helper window shows while it works.
    public interface IUpdateProgress
    {
        void Status(string text);
        void Problem(string text);
        void Finish();
    }

    // The helper started with --apply-update: waits for PxeVice to close, installs the MSI or replaces the exe, and starts PxeVice again.
    public sealed class UpdateJob
    {
        readonly bool msi;
        readonly string file, target;
        readonly int pid;
        public readonly string Version;
        public int Result = 1;

        public UpdateJob(string[] args)
        {
            msi = args[1] == "msi";
            file = args[2];
            target = args[3];
            int.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out pid);
            Version = Updater.FolderVersion(file);
        }

        public static bool Is(string[] args)
        {
            return args != null && args.Length >= 5 && args[0] == "--apply-update" && (args[1] == "msi" || args[1] == "exe");
        }

        public void Run(IUpdateProgress ui)
        {
            string problem = null;
            try
            {
                ui.Status("Waiting for PxeVice to close…");
                System.Diagnostics.Process parent = null;
                try { parent = System.Diagnostics.Process.GetProcessById(pid); } catch (Exception) { }
                if (parent != null)
                    using (parent)
                    {
                        bool gone = false;
                        try { gone = parent.WaitForExit(15000); } catch (Exception) { gone = true; }
                        if (!gone)
                        {
                            ui.Status("PxeVice is still open. Closing it…");
                            CloseOrKill(parent);
                        }
                    }
                if (msi)
                {
                    ui.Status("Installing PxeVice" + (Version != null ? " " + Version : "") + ". Windows asks for administrator rights.");
                    problem = InstallMsi();
                }
                else
                {
                    ui.Status("Replacing PxeVice.exe…");
                    problem = ReplaceExe();
                }
            }
            catch (Exception ex) { problem = "The update stopped: " + ex.Message; }
            if (problem != null) ui.Problem(problem);
            ui.Status("Starting PxeVice…");
            try
            {
                System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo(target);
                start.UseShellExecute = false;
                start.WorkingDirectory = Path.GetDirectoryName(target);
                System.Diagnostics.Process.Start(start);
            }
            catch (Exception ex) { ui.Problem("PxeVice could not start again: " + ex.Message); }
            Result = problem == null ? 0 : 1;
            ui.Finish();
        }

        static void CloseOrKill(System.Diagnostics.Process p)
        {
            try { if (p.HasExited) return; } catch (Exception) { return; }
            try { p.CloseMainWindow(); } catch (Exception) { }
            try { if (p.WaitForExit(8000)) return; } catch (Exception) { return; }
            try
            {
                p.Kill();
                p.WaitForExit(5000);
            }
            catch (Exception) { }
        }

        string InstallMsi()
        {
            string log = Path.Combine(Path.GetDirectoryName(file), "install.log");
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe"),
                    "/i \"" + file + "\" /passive /norestart /l*v \"" + log + "\"");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    p.WaitForExit();
                    int code = p.ExitCode;
                    if (code == 1618) return "Another installation is running. Try again when it has finished.";
                    if (code != 0 && code != 3010 && code != 1641)
                        return "The installation ended with code " + code + ". The installer log is " + log + ".";
                }
                return null;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return ex.NativeErrorCode == 1223 ? "The update was not installed, because administrator rights were not given." : "The installer could not start: " + ex.Message;
            }
        }

        string ReplaceExe()
        {
            Exception last = null;
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    File.Copy(file, target, true);
                    return null;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(500);
                }
            }
            return "PxeVice could not replace " + target + ": " + last.Message + "\r\n\r\nThe new version is in " + file + ". Copy it there yourself.";
        }
    }
}
