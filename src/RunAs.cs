namespace PxeVice
{
    using System;
    using System.ComponentModel;
    using System.Runtime.InteropServices;
    using System.Security.Principal;

    // Uses a service account for the network only, like runas /netonly, inside this PxeVice: on this PC it is still the
    // supporter, and what PxeVice does over the network (the SMS Provider, the site database, SMSPXE.log and the PXE
    // service) is done as the service account. Nothing is restarted, and the Secondary Logon service is not needed.
    public static class RunAs
    {
        public const string UserPref = "RunAsUser";
        public const string PasswordPref = "RunAsPassword";

        // The service account in use, or null.
        public static string Account;
        static string password;
        static IntPtr token = IntPtr.Zero;
        static readonly object gate = new object();

        // For WMI, which takes the account and password itself.
        public static string Password
        {
            get { return password; }
        }

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

        const int LogonNewCredentials = 9;
        const int ProviderWinNT50 = 3;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool LogonUser(string user, string domain, string password, int logonType, int logonProvider, out IntPtr token);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        // Starts using the account. Windows does not check the password here: it is checked the first time PxeVice
        // connects to a server, like runas /netonly.
        public static void Use(string account, string pw)
        {
            string user, domain;
            if (!Split(account, out user, out domain)) throw new ArgumentException("Write the account as DOMAIN\\name or name@domain.");
            IntPtr t;
            if (!LogonUser(user, domain, pw, LogonNewCredentials, ProviderWinNT50, out t)) throw new Win32Exception(Marshal.GetLastWin32Error());
            lock (gate)
            {
                if (token != IntPtr.Zero) CloseHandle(token);
                token = t;
                Account = account.Trim();
                password = pw;
            }
        }

        public static void Stop()
        {
            lock (gate)
            {
                if (token != IntPtr.Zero) CloseHandle(token);
                token = IntPtr.Zero;
                Account = null;
                password = null;
            }
        }

        sealed class Context : IDisposable
        {
            WindowsImpersonationContext ctx;

            public Context(WindowsImpersonationContext c)
            {
                ctx = c;
            }

            public void Dispose()
            {
                if (ctx == null) return;
                ctx.Undo();
                ctx.Dispose();
                ctx = null;
            }
        }

        sealed class None : IDisposable
        {
            public void Dispose()
            {
            }
        }

        // Network access in the using block is done as the account. Without an account it does nothing.
        public static IDisposable Impersonate()
        {
            IntPtr t;
            lock (gate) t = token;
            if (t == IntPtr.Zero) return new None();
            return new Context(WindowsIdentity.Impersonate(t));
        }

        public static bool IsLocal(string server)
        {
            if (string.IsNullOrEmpty(server)) return true;
            string s = server.Trim().TrimStart('\\').Split('.')[0];
            return string.Equals(s, Environment.MachineName, StringComparison.OrdinalIgnoreCase) || string.Equals(s, "localhost", StringComparison.OrdinalIgnoreCase) || s == "127" || s == "." || s.Length == 0;
        }
    }
}
