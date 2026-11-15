namespace PxeVice
{
    using System;
    using System.Collections.Generic;

    // Command line use, for scripts and for supporters who prefer a console.
    public static class Cli
    {
        public const string Developer = "Daniel L. Baldissera";

        public static string Version
        {
            get
            {
                object[] a = typeof(Cli).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
                return a.Length > 0 ? ((System.Reflection.AssemblyInformationalVersionAttribute)a[0]).InformationalVersion : typeof(Cli).Assembly.GetName().Version.ToString();
            }
        }

        public static string UserName
        {
            get { return Environment.UserDomainName + "\\" + Environment.UserName; }
        }

        public static ISite Connect(bool demo, SiteOptions o)
        {
            if (demo)
            {
                DemoSite d = new DemoSite();
                d.Delay = 250;
                return d;
            }
            return new LiveSite(o);
        }

        public static bool IsDemo(string a)
        {
            return string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "/demo", StringComparison.OrdinalIgnoreCase);
        }

        public static readonly string[][] Help =
        {
            new string[] { "PxeVice.exe", "Open the window" },
            new string[] { "/list", "List machines with leftover PXE data or a PXE boot that was not serviced, newest first" },
            new string[] { "/requests", "List the PXE requests in SMSPXE.log, newest first" },
            new string[] { "/hours <n>", "With /requests: the last n hours (default 24)" },
            new string[] { "/check /mac <mac>", "Check why a machine does not PXE boot. Also with /guid or /name" },
            new string[] { "/report <file.html>", "Save a report of a machine (with /mac, /guid or /name) or of every listed machine" },
            new string[] { "/reset /mac <mac>", "Reset a machine by MAC address" },
            new string[] { "/reset /guid <guid>", "Reset a machine by SMBIOS GUID" },
            new string[] { "/reset /name <name>", "Reset a device by name" },
            new string[] { "/whatif", "Show what /reset would change, and change nothing" },
            new string[] { "/removedevice", "Also delete named device records that match" },
            new string[] { "/keepunknown", "Do not delete device records named Unknown" },
            new string[] { "/restartpxe", "Restart the PXE service afterwards" },
            new string[] { "/use <name>", "Use a site saved in PxeVice" },
            new string[] { "/site <server>", "The site server with the SMS Provider (default: this computer or Settings)" },
            new string[] { "/sql <server> /db <name>", "The site database, when it is not found from the SMS Provider" },
            new string[] { "/pxe <servers>", "PXE distribution points, separated by commas" },
            new string[] { "--demo", "Use sample data" },
        };

        public static void Usage()
        {
            Console.WriteLine("PxeVice " + Cli.Version + " - clear leftover PXE data so a machine can PXE boot again");
            Console.WriteLine();
            foreach (string[] h in Help) Console.WriteLine("  " + h[0].PadRight(28) + h[1]);
            Console.WriteLine();
            Console.WriteLine("Exit codes: 0 done, 1 error, 2 nothing found for the machine, or for /check: something blocks PXE");
        }

        public static int Run(string[] args)
        {
            bool list = false, reset = false, whatIf = false, demo = false, check = false, requests = false;
            string mac = null, guid = null, name = null, report = null;
            int hours = 24;
            ResetOptions ro = new ResetOptions();
            SiteOptions so = SiteOptions.FromPrefs();
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (Cli.IsDemo(args[i])) { demo = true; continue; }
                    string a = args[i].TrimStart('/', '-').ToLowerInvariant();
                    switch (a)
                    {
                        case "list": list = true; break;
                        case "reset": reset = true; break;
                        case "check": check = true; break;
                        case "requests": requests = true; break;
                        case "report": report = Next(args, ref i); break;
                        case "hours":
                            if (!int.TryParse(Next(args, ref i), out hours) || hours < 0) throw new ArgumentException("Not a number of hours: " + args[i]);
                            break;
                        case "use":
                            SavedSite saved = Sites.Find(Next(args, ref i));
                            if (saved == null) throw new ArgumentException("No saved site is named " + args[i] + ".");
                            so.Name = saved.Name;
                            so.SiteServer = saved.SiteServer;
                            so.SqlServer = saved.SqlServer;
                            so.Database = saved.Database;
                            so.PxeServers = saved.PxeServers;
                            if (saved.PxeLogPath != null) Prefs.Set("PxeLogPath", saved.PxeLogPath);
                            break;
                        case "mac":
                            mac = Ids.Mac(Next(args, ref i));
                            if (mac == null) throw new ArgumentException("Not a MAC address: " + args[i]);
                            break;
                        case "guid":
                            guid = Ids.Guid(Next(args, ref i));
                            if (guid == null) throw new ArgumentException("Not an SMBIOS GUID: " + args[i]);
                            break;
                        case "name": name = Next(args, ref i); break;
                        case "whatif": whatIf = true; break;
                        case "removedevice": ro.DeleteDevices = true; break;
                        case "keepunknown": ro.DeleteUnknownRecords = false; break;
                        case "restartpxe": ro.RestartPxeService = true; break;
                        case "site": so.SiteServer = Next(args, ref i); break;
                        case "sql": so.SqlServer = Next(args, ref i); break;
                        case "db": so.Database = Next(args, ref i); break;
                        case "pxe": so.PxeServers = Next(args, ref i); break;
                        case "?":
                        case "h":
                        case "help": Usage(); return 0;
                        default: throw new ArgumentException("Unknown option: " + args[i]);
                    }
                }
                if (!list && !reset && !check && !requests && report == null)
                {
                    Usage();
                    return 1;
                }
                bool target = mac != null || guid != null || !string.IsNullOrEmpty(name);
                if ((reset || check) && !target) throw new ArgumentException((reset ? "/reset" : "/check") + " needs /mac, /guid or /name.");
                using (ISite site = Cli.Connect(demo, so))
                {
                    Console.WriteLine(site.Info.Describe(" | "));
                    foreach (string n in site.Info.Notes) Console.WriteLine("Note: " + n);
                    Console.WriteLine();
                    PxeLogStore store = new PxeLogStore();
                    SiteData d = Engine.Read(site, store);
                    foreach (string n in d.Notes) Console.WriteLine("Note: " + n);
                    if (list) return List(d);
                    if (requests) return Requests(d, hours);
                    if (report != null) return Save(site, d, mac, guid, name, report);
                    if (check) return Check(site, d, mac, guid, name);
                    return Reset(site, d, mac, guid, name, ro, whatIf);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PxeVice: " + ex.Message);
                return 1;
            }
        }

        static int List(SiteData d)
        {
            Console.WriteLine("Last seen".PadRight(20) + "Name".PadRight(18) + "Found".PadRight(28) + "MAC address".PadRight(19) + "SMBIOS GUID".PadRight(38) + "Deployment");
            foreach (Machine m in d.Machines)
            {
                Console.WriteLine(Plain(Txt.Local(m.LastSeenUtc).PadRight(20) + Txt.Short(m.Name, 17).PadRight(18) + Txt.Short(m.Found, 27).PadRight(28) +
                    (m.MacAddress ?? "").PadRight(19) + (m.SmbiosGuid ?? "").PadRight(38) + m.Deployments));
            }
            Console.WriteLine(d.Machines.Count + " machines, " + d.Flags.Count + " PXE flags, " + d.UnknownRecords.Count + " Unknown device records, " + d.Requests.Count + " PXE requests");
            return 0;
        }

        // The console's code page may not have the ellipsis.
        static string Plain(string s)
        {
            return s.Replace("\u2026", "...");
        }

        static int Requests(SiteData d, int hours)
        {
            DateTime since = DateTime.UtcNow.AddHours(-hours);
            Console.WriteLine("Time".PadRight(20) + "Server".PadRight(20) + "MAC address".PadRight(19) + "SMBIOS GUID".PadRight(38) + "Answered for".PadRight(22) + "Result");
            int n = 0;
            foreach (PxeRequest r in d.Requests)
            {
                if (hours > 0 && r.Utc.HasValue && r.Utc.Value < since) continue;
                n++;
                Console.WriteLine(Plain(Txt.Local(r.Utc).PadRight(20) + Txt.Short(r.Server, 19).PadRight(20) + (r.MacAddress ?? "").PadRight(19) + (r.SmbiosGuid ?? "").PadRight(38) + Txt.Short(r.Identity, 21).PadRight(22) + r.Result));
            }
            Console.WriteLine(n + " PXE requests" + (hours > 0 ? " in the last " + hours + " hours" : ""));
            return 0;
        }

        // The listed machine that matches, or one made from what the site has for it.
        public static Machine Resolve(ISite site, SiteData d, string mac, string guid, string name)
        {
            foreach (Machine m in d.Machines)
            {
                if (guid != null && m.SmbiosGuid == guid) return m;
                if (mac != null && guid == null && m.AllMacs.Contains(mac)) return m;
                if (!string.IsNullOrEmpty(name) && mac == null && guid == null && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
            }
            Machine t = Target(site, mac, guid, name);
            if (t == null) return null;
            foreach (PxeRequest r in d.Requests)
            {
                if ((t.SmbiosGuid != null && r.SmbiosGuid == t.SmbiosGuid) || (r.SmbiosGuid == null || t.SmbiosGuid == null) && t.MacAddress != null && r.MacAddress == t.MacAddress) t.Requests.Add(r);
            }
            t.DeploymentNames = d.DeploymentNames;
            t.Key = Engine.Key(t.MacAddress, t.SmbiosGuid);
            return t;
        }

        static int Check(ISite site, SiteData d, string mac, string guid, string name)
        {
            Machine m = Resolve(site, d, mac, guid, name);
            if (m == null)
            {
                Console.WriteLine("No device named " + name + " was found.");
                return 2;
            }
            List<Finding> f = Checker.Check(site, m, new CheckCache(), DateTime.UtcNow);
            Console.Write(Plain(Checker.AsText(m, f, DateTime.UtcNow)));
            return Checker.Blocked(f) ? 2 : 0;
        }

        static int Save(ISite site, SiteData d, string mac, string guid, string name, string file)
        {
            List<Machine> list = new List<Machine>();
            if (mac != null || guid != null || !string.IsNullOrEmpty(name))
            {
                Machine m = Resolve(site, d, mac, guid, name);
                if (m == null)
                {
                    Console.WriteLine("No device named " + name + " was found.");
                    return 2;
                }
                list.Add(m);
            }
            else list.AddRange(d.Machines);
            CheckCache cache = new CheckCache();
            Dictionary<string, List<Finding>> checks = new Dictionary<string, List<Finding>>(StringComparer.OrdinalIgnoreCase);
            foreach (Machine m in list) checks[m.Key ?? ""] = Checker.Check(site, m, cache, DateTime.UtcNow);
            string title = list.Count == 1 ? "PXE check of " + list[0].Name : "PXE check of " + list.Count + " machines";
            System.IO.File.WriteAllText(file, Report.Html(site.Info, list, checks, null, DateTime.UtcNow, title), new System.Text.UTF8Encoding(false));
            Console.WriteLine("Saved " + file);
            return 0;
        }

        static int Reset(ISite site, SiteData d, string mac, string guid, string name, ResetOptions ro, bool whatIf)
        {
            Machine m = Resolve(site, d, mac, guid, name);
            if (m == null)
            {
                Console.WriteLine("No device named " + name + " was found.");
                return 2;
            }
            Plan p = Engine.BuildPlan(site, new List<Machine>(new Machine[] { m }), ro);
            foreach (string n in p.Notes) Console.WriteLine(n);
            if (p.Steps.Count == 0)
            {
                Console.WriteLine("Nothing to reset.");
                return 2;
            }
            if (whatIf)
            {
                foreach (Step s in p.Steps) Console.WriteLine("What if: " + s.Text);
                return 0;
            }
            int failed = 0;
            Engine.Execute(site, p, delegate (Activity a)
            {
                if (!a.Ok) failed++;
                Console.WriteLine((a.Ok ? "Done:   " : "Failed: ") + a.Action + ". " + a.Result);
            });
            Console.WriteLine("Logged to " + ActivityLog.FilePath);
            return failed > 0 ? 1 : 0;
        }

        // The machine to reset, from what the user gave: a MAC address, an SMBIOS GUID or a device name.
        public static Machine Target(ISite site, string mac, string guid, string name)
        {
            Machine m = new Machine();
            m.MacAddress = mac;
            m.SmbiosGuid = guid;
            if (!string.IsNullOrEmpty(name))
            {
                List<DeviceRecord> named = site.FindDevices(null, null, name, null);
                if (named.Count == 0 && mac == null && guid == null) return null;
                foreach (DeviceRecord d in named)
                {
                    m.Devices.Add(d);
                    if (m.SmbiosGuid == null) m.SmbiosGuid = d.SmbiosGuid;
                    if (m.MacAddress == null && d.MacAddresses.Count > 0) m.MacAddress = d.MacAddresses[0];
                }
            }
            if (m.MacAddress == null && m.SmbiosGuid == null) return m;
            // With only a MAC address, use the machine's SMBIOS GUID when its flags and records agree on one,
            // so the reset does not reach other machines that boot with the same network adapter.
            if (m.SmbiosGuid == null)
            {
                List<string> guids = new List<string>();
                foreach (PxeFlag f in site.FlagsFor(m.MacAddress, null, null)) if (f.SmbiosGuid != null && !guids.Contains(f.SmbiosGuid)) guids.Add(f.SmbiosGuid);
                foreach (DeviceRecord d in site.FindDevices(m.MacAddress, null, null, null)) if (d.SmbiosGuid != null && !guids.Contains(d.SmbiosGuid)) guids.Add(d.SmbiosGuid);
                if (guids.Count == 1) m.SmbiosGuid = guids[0];
            }
            m.Flags.AddRange(site.FlagsFor(m.MacAddress, m.SmbiosGuid, null));
            foreach (DeviceRecord d in site.FindDevices(m.MacAddress, m.SmbiosGuid, null, null))
            {
                bool have = false;
                foreach (DeviceRecord x in m.Devices) if (x.ResourceId == d.ResourceId) have = true;
                if (!have) m.Devices.Add(d);
            }
            return m;
        }

        static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("Missing value after " + args[i]);
            return args[++i];
        }
    }
}
