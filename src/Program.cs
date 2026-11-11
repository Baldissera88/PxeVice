namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;

    public static class Program
    {
        public const string Developer = "Daniel L. Baldissera";
        public const string BuildDate = "11 November 2026";

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
            using (MainForm f = new MainForm(demo))
            {
                Application.Run(f);
            }
            return 0;
        }
    }
}
