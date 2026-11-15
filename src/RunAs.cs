namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Runtime.InteropServices;
    using System.Text;

    // Runs PxeVice with a service account for the network only, like runas /netonly: on this PC it is still the
    // supporter, and everything it does over the network (the SMS Provider, the site database, SMSPXE.log and the
    // PXE service) is done as the service account.
    public static class RunAs
    {
        public const string Arg = "--as";
        public const string UserPref = "RunAsUser";
        public const string PasswordPref = "RunAsPassword";

        // The service account this PxeVice was started with, or null.
        public static string Account;

        // DOMAIN\user or user@domain, split into the user and the domain. A name without a domain is a local account.
        public static bool Split(string account, out string user, out string domain)
        {
            user = null;
            domain = null;
            if (string.IsNullOrEmpty(account)) return false;
            string a = account.Trim();
            int slash = a.IndexOf('\\');
            if (slash > 0 && slash < a.Length - 1)
            {
                domain = a.Substring(0, slash);
                user = a.Substring(slash + 1);
                return true;
            }
            if (slash >= 0) return false;
            int at = a.IndexOf('@');
            if (at > 0 && at < a.Length - 1)
            {
                // The domain goes in the user name, and the domain parameter stays empty.
                user = a;
                domain = null;
                return true;
            }
            if (at >= 0) return false;
            user = a;
            domain = ".";
            return true;
        }

        // Quotes one argument the way CommandLineToArgvW reads it back.
        public static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) return arg;
            StringBuilder b = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') b.Append('\\', slashes * 2 + 1);
                else b.Append('\\', slashes);
                slashes = 0;
                b.Append(c);
            }
            b.Append('\\', slashes * 2).Append('"');
            return b.ToString();
        }

        // The arguments for the new PxeVice: the ones given, without an earlier --as, and --as with the account.
        public static string Arguments(string[] args, string account)
        {
            List<string> l = new List<string>();
            for (int i = 0; args != null && i < args.Length; i++)
            {
                if (string.Equals(args[i], Arg, StringComparison.OrdinalIgnoreCase)) { i++; continue; }
                l.Add(Quote(args[i]));
            }
            l.Add(Arg);
            l.Add(Quote(account));
            return string.Join(" ", l.ToArray());
        }

        // Takes --as <account> from the arguments and keeps the account. Returns the other arguments.
        public static string[] Take(string[] args)
        {
            List<string> l = new List<string>();
            for (int i = 0; args != null && i < args.Length; i++)
            {
                if (string.Equals(args[i], Arg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    Account = args[++i];
                    continue;
                }
                l.Add(args[i]);
            }
            return l.ToArray();
        }

        const int LogonNetCredentialsOnly = 2;
        const int CreateUnicodeEnvironment = 0x400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct StartupInfo
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessInformation
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateProcessWithLogonW(string user, string domain, string password, int logonFlags, string application, StringBuilder commandLine,
            int creationFlags, IntPtr environment, string currentDirectory, ref StartupInfo startup, out ProcessInformation info);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        // Starts PxeVice again with the service account for the network. The password is not checked here: Windows
        // checks it the first time PxeVice connects to the site.
        public static void Start(string exe, string[] args, string account, string password)
        {
            string user, domain;
            if (!Split(account, out user, out domain)) throw new ArgumentException("Write the account as DOMAIN\\name or name@domain.");
            StartupInfo si = new StartupInfo();
            si.cb = Marshal.SizeOf(typeof(StartupInfo));
            ProcessInformation pi;
            StringBuilder cmd = new StringBuilder(Quote(exe) + " " + Arguments(args, account));
            if (!CreateProcessWithLogonW(user, domain, password, LogonNetCredentialsOnly, exe, cmd, CreateUnicodeEnvironment, IntPtr.Zero, System.IO.Path.GetDirectoryName(exe), ref si, out pi))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
        }

        public static bool IsLocal(string server)
        {
            if (string.IsNullOrEmpty(server)) return true;
            string s = server.Trim().Split('.')[0];
            return string.Equals(s, Environment.MachineName, StringComparison.OrdinalIgnoreCase) || s == "localhost" || s == "127" || s == ".";
        }
    }
}
