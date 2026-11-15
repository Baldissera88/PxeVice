namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;

    public static class Program
    {
        public const string Developer = Cli.Developer;
        public const string BuildDate = "15 November 2026";

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
            string[] given = args ?? new string[0];
            args = RunAs.Take(given);
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
            // With a service account in Settings, PxeVice starts itself again with it for the network.
            if (!demo && RunAs.Account == null && !string.IsNullOrEmpty(Prefs.Get(RunAs.UserPref, null)) && StartWithAccount(args, false)) return 0;
            using (MainForm f = new MainForm(demo))
            {
                Application.Run(f);
                if (f.RestartWithAccount && StartWithAccount(args, true)) return 0;
            }
            return 0;
        }

        // Asks for the password when it is not remembered, and starts PxeVice with the account. Returns false when it
        // did not start, so this PxeVice goes on with the user's own account.
        public static bool StartWithAccount(string[] args, bool ask)
        {
            string account = Prefs.Get(RunAs.UserPref, null);
            if (string.IsNullOrEmpty(account)) return false;
            string password = Secret.Load(RunAs.PasswordPref);
            Theme t = Theme.Named(Prefs.Get("Theme", "System"));
            if (password.Length == 0 || ask)
            {
                using (NameDialog d = new NameDialog(t, "PxeVice with " + account, "Password", "The password of " + account + ". PxeVice uses the account for the network only: the site, the site database and the PXE distribution points. Cancel to go on with your own account.",
                    password, true, "Keep it for your Windows user, encrypted with DPAPI, so PxeVice starts with the account without asking."))
                {
                    if (d.ShowDialog() != DialogResult.OK || d.Value.Length == 0) return false;
                    password = d.Value;
                    Secret.Save(RunAs.PasswordPref, d.Remember ? password : null);
                }
            }
            try
            {
                RunAs.Start(Location, args, account, password);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("PxeVice could not start with " + account + ": " + ex.Message + "\n\nIt goes on with your own account.", "PxeVice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }
    }
}
