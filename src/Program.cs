namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;

    // What a screenshot run asks for: --shot <file>, --page <name>, --size <W>x<H>, --select <machine> and --tab <n>.
    public sealed class StartOptions
    {
        public string Shot, Page, Select;
        public int Tab = -1;
        public System.Drawing.Size Size;
    }

    public static class Program
    {
        public static readonly StartOptions Start = new StartOptions();
        public const string Developer = Cli.Developer;
        // A theme given with --theme for this run only, or null.
        public static string ThemeOverride;

        public const string BuildDate = "23 November 2026";

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        public static string Location
        {
            get
            {
                try
                {
                    string p = typeof(Program).Assembly.Location;
                    return string.IsNullOrEmpty(p) ? Application.ExecutablePath : p;
                }
                catch (Exception) { return Application.ExecutablePath; }
            }
        }

        public static bool IsAdmin
        {
            get
            {
                try
                {
                    return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch (Exception) { return false; }
            }
        }

        // Starts PxeVice again with the same arguments and the Windows prompt for administrator rights. False when the prompt was
        // declined or PxeVice could not start; the reason is in error.
        public static bool RelaunchAsAdmin(out string error)
        {
            error = null;
            try
            {
                string[] all = Environment.GetCommandLineArgs();
                System.Text.StringBuilder a = new System.Text.StringBuilder();
                for (int i = 1; i < all.Length; i++)
                {
                    if (a.Length > 0) a.Append(' ');
                    a.Append(all[i].IndexOfAny(new char[] { ' ', '\t', '"' }) >= 0 ? "\"" + all[i].Replace("\"", "\\\"") + "\"" : all[i]);
                }
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(Location, a.ToString());
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WorkingDirectory = System.IO.Path.GetDirectoryName(Location);
                System.Diagnostics.Process.Start(psi);
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                error = ex.NativeErrorCode == 1223 ? "Administrator rights were not given, so PxeVice stays as it is." : "PxeVice could not start as administrator: " + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = "PxeVice could not start as administrator: " + ex.Message;
                return false;
            }
        }

        static Icon icon;

        public static Icon AppIcon()
        {
            if (icon != null) return icon;
            try
            {
                using (System.IO.Stream s = typeof(Program).Assembly.GetManifestResourceStream("PxeVice.ico"))
                {
                    if (s != null) icon = new Icon(s);
                }
            }
            catch (Exception) { }
            return icon;
        }

        public static string OsVersion()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                using (Microsoft.Win32.RegistryKey k = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        string name = Convert.ToString(k.GetValue("ProductName"));
                        string disp = Convert.ToString(k.GetValue("DisplayVersion"));
                        string build = Convert.ToString(k.GetValue("CurrentBuild"));
                        int b;
                        if (int.TryParse(build, out b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
                        return (name + " " + disp).Trim() + " (" + build + ")";
                    }
                }
            }
            catch (Exception) { }
            return Environment.OSVersion.VersionString;
        }

        [STAThread]
        public static int Main(string[] args)
        {
            args = args ?? new string[0];
            // --theme <name> (or /theme <name>) sets the theme for this run only.
            {
                List<string> rest = new List<string>();
                for (int i = 0; i < args.Length; i++)
                {
                    string low = args[i].ToLowerInvariant();
                    if ((low == "--theme" || low == "/theme") && i + 1 < args.Length)
                    {
                        string want = args[++i];
                        if (Palettes.Find(want) != null || string.Equals(want, "system", StringComparison.OrdinalIgnoreCase)) ThemeOverride = want;
                    }
                    else if (low == "--shot" && i + 1 < args.Length) Start.Shot = args[++i];
                    else if (low == "--page" && i + 1 < args.Length) Start.Page = args[++i];
                    else if (low == "--select" && i + 1 < args.Length) Start.Select = args[++i];
                    else if (low == "--tab" && i + 1 < args.Length) int.TryParse(args[++i], out Start.Tab);
                    else if (low == "--size" && i + 1 < args.Length)
                    {
                        string[] wh = args[++i].Split('x');
                        int w, h;
                        if (wh.Length == 2 && int.TryParse(wh[0], out w) && int.TryParse(wh[1], out h)) Start.Size = new System.Drawing.Size(w, h);
                    }
                    else rest.Add(args[i]);
                }
                args = rest.ToArray();
            }
            if (UpdateJob.Is(args))
            {
                // The helper of an update: PxeVice has closed, or is about to.
                try { SetProcessDPIAware(); } catch (Exception) { }
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                }
                catch (InvalidOperationException) { }
                try
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Dpi.Scale = Math.Max(1f, g.DpiX / 96f);
                }
                catch (Exception) { }
                UpdateJob job = new UpdateJob(args);
                Application.Run(new UpdateWindow(job));
                return job.Result;
            }
            bool demo = false;
            foreach (string a in args ?? new string[0]) if (Cli.IsDemo(a)) demo = true;
            bool cli = false;
            foreach (string a in args ?? new string[0]) if ((a.StartsWith("/") || a.StartsWith("-")) && !Cli.IsDemo(a)) cli = true;
            if (cli)
            {
                try { AttachConsole(-1); } catch (Exception) { }
                Console.WriteLine();
                return Cli.Run(args);
            }
            AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                MessageBox.Show(ex != null ? ex.ToString() : "Unknown error", "PxeVice", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            try { SetProcessDPIAware(); } catch (Exception) { }
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
            }
            catch (InvalidOperationException) { }
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Dpi.Scale = Math.Max(1f, g.DpiX / 96f);
            }
            catch (Exception) { }
            Fonts.Init();
            try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); } catch (Exception) { }
            Application.ThreadException += delegate (object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                if (Safe.Reporter != null) Safe.Report(e.Exception);
                else MessageBox.Show(e.Exception.Message, "PxeVice", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            // With a service account in Settings, PxeVice uses it for the network from the start.
            if (!demo && !string.IsNullOrEmpty(Prefs.Get(RunAs.UserPref, null))) UseAccount(null, false);
            using (MainForm f = new MainForm(demo))
            {
                Application.Run(f);
            }
            RunAs.Stop();
            return 0;
        }

        // Asks for the password when it is not remembered or when asked to, and starts using the account. Returns false
        // when it is not used, so PxeVice goes on with the user's own account.
        public static bool UseAccount(IWin32Window owner, bool ask)
        {
            string account = Prefs.Get(RunAs.UserPref, null);
            if (string.IsNullOrEmpty(account)) return false;
            string password = Secret.Load(RunAs.PasswordPref);
            Theme t = Theme.Named(ThemeOverride ?? Theme.SavedName());
            if (password.Length == 0 || ask)
            {
                using (NameDialog d = new NameDialog(t, "PxeVice with " + account, "Password", "The password of " + account + ". PxeVice uses the account for the network only: the site, the site database and the PXE distribution points. Cancel to go on with your own account.",
                    password, true, "Keep it for your Windows user, encrypted with DPAPI, so PxeVice uses the account without asking."))
                {
                    if ((owner == null ? d.ShowDialog() : d.ShowDialog(owner)) != DialogResult.OK || d.Value.Length == 0) return false;
                    password = d.Value;
                    Secret.Save(RunAs.PasswordPref, d.Remember ? password : null);
                }
            }
            try
            {
                RunAs.Use(account, password);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("PxeVice could not use " + account + ": " + ex.Message + "\n\nIt goes on with your own account.", "PxeVice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }
    }
}
