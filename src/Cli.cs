namespace PxeVice
{
    using System;
    using System.Collections.Generic;

    // Command line use, for scripts and for supporters who prefer a console.
    public static class Cli
    {
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
            new string[] { "/list", "List machines with leftover PXE data, newest first" },
            new string[] { "/reset /mac <mac>", "Reset a machine by MAC address" },
            new string[] { "/reset /guid <guid>", "Reset a machine by SMBIOS GUID" },
            new string[] { "/reset /name <name>", "Reset a device by name" },
            new string[] { "/whatif", "Show what /reset would change, and change nothing" },
            new string[] { "/removedevice", "Also delete named device records that match" },
            new string[] { "/keepunknown", "Do not delete device records named Unknown" },
            new string[] { "/restartpxe", "Restart the PXE service afterwards" },
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
            Console.WriteLine("Exit codes: 0 done, 1 error, 2 nothing found for the machine");
        }

        public static int Run(string[] args)
        {
            bool list = false, reset = false, whatIf = false, demo = false;
            string mac = null, guid = null, name = null;
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
                if (!list && !reset)
                {
                    Usage();
                    return 1;
                }
                if (reset && mac == null && guid == null && string.IsNullOrEmpty(name)) throw new ArgumentException("/reset needs /mac, /guid or /name.");
                using (ISite site = Cli.Connect(demo, so))
                {
                    Console.WriteLine(site.Info.Describe(" | "));
                    foreach (string n in site.Info.Notes) Console.WriteLine("Note: " + n);
                    Console.WriteLine();
                    if (list) return List(site);
                    return Reset(site, mac, guid, name, ro, whatIf);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PxeVice: " + ex.Message);
                return 1;
            }
        }

        static int List(ISite site)
        {
            SiteData d = Engine.Read(site);
            Console.WriteLine("Last seen".PadRight(20) + "Name".PadRight(18) + "Found".PadRight(28) + "MAC address".PadRight(19) + "SMBIOS GUID".PadRight(38) + "Deployment");
            foreach (Machine m in d.Machines)
            {
                Console.WriteLine(Txt.Local(m.LastSeenUtc).PadRight(20) + Txt.Short(m.Name, 17).PadRight(18) + Txt.Short(m.Found, 27).PadRight(28) +
                    (m.MacAddress ?? "").PadRight(19) + (m.SmbiosGuid ?? "").PadRight(38) + m.Deployments);
            }
            Console.WriteLine(d.Machines.Count + " machines, " + d.Flags.Count + " PXE flags, " + d.UnknownRecords.Count + " Unknown device records");
            return 0;
        }

        static int Reset(ISite site, string mac, string guid, string name, ResetOptions ro, bool whatIf)
        {
            Machine m = Target(site, mac, guid, name);
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
