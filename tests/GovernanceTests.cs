namespace PxeVice.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    public static class GovernanceTests
    {
        static readonly DateTime Now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

        public static void Run()
        {
            try
            {
                Tickets();
                Policies();
                Backups();
                Keys();
                History();
                HealthChecks();
                PolicyFiles();
            }
            finally { PxePolicy.Read = delegate (string n) { return 0; }; }
        }

        static void Policy(params object[] pairs)
        {
            Dictionary<string, long> v = new Dictionary<string, long>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) v[(string)pairs[i]] = Convert.ToInt64(pairs[i + 1]);
            PxePolicy.Read = delegate (string n) { long x; return v.TryGetValue(n, out x) ? x : 0; };
        }

        static Machine Demo(out DemoSite site, out Plan plan, bool deleteDevices)
        {
            site = new DemoSite(Now);
            Machine m = null;
            foreach (Machine x in Engine.Read(site).Machines) if (x.MacAddress == "F8:75:A4:0B:7E:21") m = x;
            ResetOptions o = new ResetOptions();
            o.DeleteDevices = deleteDevices;
            plan = Engine.BuildPlan(site, new List<Machine>(new Machine[] { m }), o);
            return m;
        }

        static void Tickets()
        {
            T.Equal("INC0012345", Ticket.Clean("  INC0012345 "), "a ticket is trimmed");
            T.Equal("a b", Ticket.Clean("a\r\n\tb"), "line breaks and tabs become one space");
            T.Equal("a b", Ticket.Clean("a[b"), "brackets are taken out, so a ticket cannot fake a log line");
            T.Equal("", Ticket.Clean(null), "null is empty");
            T.Equal(Ticket.MaxLength, Ticket.Clean(new string('x', 200)).Length, "a ticket is at most 60 characters");
        }

        static void Policies()
        {
            DemoSite site; Plan plan;
            Demo(out site, out plan, true);
            Policy();
            T.Check(PxePolicy.Blocked(plan, 1, "") == null, "no policy blocks nothing");
            Policy(PxePolicy.ReadOnlyName, 1);
            T.Check(PxePolicy.Blocked(plan, 1, "T1").Contains("read only"), "ReadOnly blocks a reset");
            Policy(PxePolicy.NoDeleteName, 1);
            T.Check(PxePolicy.Blocked(plan, 1, "").Contains("delete devices"), "DisableDeviceDeletion blocks a plan that deletes a device");
            Plan safe; Demo(out site, out safe, false);
            T.Check(PxePolicy.Blocked(safe, 1, "") == null, "DisableDeviceDeletion allows a plan without a deletion");
            Policy(PxePolicy.MaxMachinesName, 2);
            T.Check(PxePolicy.Blocked(safe, 3, "").Contains("2 machines"), "MaxMachinesPerReset blocks more machines");
            T.Check(PxePolicy.Blocked(safe, 2, "") == null, "MaxMachinesPerReset allows up to the limit");
            Policy(PxePolicy.RequireTicketName, 1);
            T.Check(PxePolicy.Blocked(safe, 1, "  ").Contains("ticket"), "RequireTicket blocks an empty ticket");
            T.Check(PxePolicy.Blocked(safe, 1, "INC1") == null, "RequireTicket allows a ticket");

            // Execute stops before it changes anything.
            Policy(PxePolicy.ReadOnlyName, 1);
            Machine m = Demo(out site, out plan, true);
            int flagsBefore = Engine.Read(site).Flags.Count;
            ResetContext ctx = new ResetContext();
            ctx.Machines.Add(m);
            List<Activity> done = Engine.Execute(site, plan, ctx, null);
            T.Equal(1, done.Count, "a blocked reset reports once");
            T.Check(!done[0].Ok && done[0].Action == "Reset not started", "a blocked reset says so and is not ok");
            T.Equal(flagsBefore, Engine.Read(site).Flags.Count, "nothing was changed on the site");
        }

        static void Backups()
        {
            Policy();
            Engine.VerifyWaitMs = 0;
            DemoSite site; Plan plan;
            Machine m = Demo(out site, out plan, true);
            string folder = Path.Combine(ResetBackup.Folder, "one");
            ResetContext ctx = new ResetContext();
            ctx.Machines.Add(m);
            ctx.Ticket = " INC77 ";
            ctx.BackupFolder = folder;
            List<Activity> done = Engine.Execute(site, plan, ctx, null);
            T.Equal("Wrote what is known before the deletion", done[0].Action, "the backup is the first thing that happens");
            string[] files = Directory.GetFiles(folder);
            T.Equal(1, files.Length, "one backup file");
            string text = File.ReadAllText(files[0]);
            T.Check(text.Contains("Ticket:  INC77") && text.Contains(m.MacAddress) && text.Contains("HIGH RISK"), "the backup holds the ticket, the MAC address and the risk");
            T.Equal("INC77", done[done.Count - 1].Ticket, "every activity carries the ticket");
            T.Check(ActivityLog.Line(done[done.Count - 1]).Contains("ticket INC77"), "the log line says the ticket");

            // No backup, no delete.
            string blocker = Path.Combine(ResetBackup.Folder, "file-not-folder");
            Directory.CreateDirectory(ResetBackup.Folder);
            File.WriteAllText(blocker, "x");
            Machine m2 = Demo(out site, out plan, true);
            ResetContext bad = new ResetContext();
            bad.Machines.Add(m2);
            bad.BackupFolder = blocker;
            int before = Engine.Read(site).Machines.Count;
            done = Engine.Execute(site, plan, bad, null);
            T.Equal(1, done.Count, "a failed backup stops the reset");
            T.Check(done[0].Result.Contains("no device is deleted"), "and says no device is deleted");
            T.Equal(before, Engine.Read(site).Machines.Count, "nothing was deleted");

            // A plan without a deletion needs no backup.
            Policy();
            Machine m3 = Demo(out site, out plan, false);
            ResetContext none = new ResetContext();
            none.Machines.Add(m3);
            none.BackupFolder = blocker;
            done = Engine.Execute(site, plan, none, null);
            T.Check(done.Count > 0 && done[0].Action != "Reset not started", "a reset that deletes no device does not need a backup");
        }

        static void Keys()
        {
            T.Check(BootKeys.Find("dell inc.").Key == "F12", "Dell is F12");
            T.Check(BootKeys.Find("Lenovo").Key.StartsWith("F12"), "Lenovo is F12");
            T.Check(BootKeys.Find("HP").Key.StartsWith("F9"), "HP is F9");
            T.Check(BootKeys.Find("nobody") == null, "an unknown maker is none");
            string msg = BootKeys.ForUser("PC-017", BootKeys.Find("Dell"));
            T.Check(msg.Contains("PC-017") && msg.Contains("F12") && !msg.Contains("SCCM") && !msg.Contains("PXE service"), "the message for the user names the machine and the key, and no jargon");
            T.Check(BootKeys.ForUser("PC-017", null).Contains("F9"), "without a maker the common keys are listed");
        }

        static string L(string text, DateTime local, bool ok)
        {
            return "<![LOG[" + text + "]LOG]!><time=\"" + local.ToString("HH:mm:ss") + ".000+060\" date=\"" + local.ToString("MM-dd-yyyy") + "\" component=\"PxeVice\" context=\"\" type=\"" + (ok ? "1" : "3") + "\" thread=\"1\" file=\"\">";
        }

        static void History()
        {
            DateTime now = new DateTime(2026, 11, 24, 12, 0, 0);
            List<string> lines = new List<string>();
            lines.Add(L("PC-1: Clear the PXE flag on PC-1 with Clear Required PXE Deployments. Cleared (by A)", now.AddDays(-9), true));
            lines.Add(L("PC-1: Delete 1 row for PC-1 from LastPXEAdvertisement. 1 row deleted (by A)", now.AddDays(-9).AddMinutes(1), true));
            lines.Add(L("PC-1: Clear the PXE flag on PC-1 with Clear Required PXE Deployments. Cleared (by A)", now.AddDays(-2), true));
            lines.Add(L("PC-2: Clear the PXE flag on PC-2 with Clear Required PXE Deployments. Cleared (by A)", now.AddDays(-1), true));
            lines.Add(L("PC-1: Clear the PXE flag on PC-1 with Clear Required PXE Deployments. Failed: access denied (by A)", now.AddHours(-1), false));
            lines.Add(L("PC-1: Clear the PXE flag on PC-1 with Clear Required PXE Deployments. Cleared (by A)", now.AddDays(-40), true));
            lines.Add("not a log line");
            List<DateTime> r = ResetHistory.Resets(lines, "PC-1", now, 14);
            T.Equal(2, r.Count, "two resets of PC-1 in 14 days: changes a minute apart are one, failures and old ones do not count");
            T.Check(ResetHistory.Repeat(r).Contains("2 times"), "a machine reset twice gets a sentence");
            T.Check(ResetHistory.Repeat(ResetHistory.Resets(lines, "PC-2", now, 14)) == null, "one reset is nothing to say");
            T.Check(ResetHistory.Repeat(null) == null, "null is nothing");
        }

        static HealthItem Item(List<HealthItem> l, string title)
        {
            foreach (HealthItem h in l) if (h.Title == title) return h;
            return null;
        }

        static void HealthChecks()
        {
            DemoSite site = new DemoSite(Now);
            List<HealthItem> items = Health.Run(site, new CheckCache(), Now);
            T.Check(Item(items, "PXE distribution point").State == HealthState.Ok, "the demo site has PXE points");
            T.Check(Item(items, "Deployment to All Unknown Computers").State == HealthState.Ok, "the demo site deploys to All Unknown Computers");
            T.Equal(0, Health.Problems(items), "the demo site has no problem");
            T.Check(Item(items, "SMSPXE.log on DP01.contoso.local").State == HealthState.Unknown, "a log PxeVice cannot find is not known, not a problem");
            T.Equal(HealthState.Unknown, Health.Worst(items), "unknown is worse than ok, better than a problem");

            // The point does not answer unknown computers, and no deployment reaches them.
            foreach (PxePoint p in site.Points) p.SupportsUnknown = false;
            site.Deployments.Clear();
            items = Health.Run(site, new CheckCache(), Now);
            T.Check(Item(items, "Unknown computer support on DP01.contoso.local").State == HealthState.Problem, "no unknown computer support is a problem");
            T.Check(Item(items, "Deployment to All Unknown Computers").State == HealthState.Problem, "no deployment to All Unknown Computers is a problem");
            T.Equal(HealthState.Problem, Health.Worst(items), "a problem is the worst");
            T.Check(Health.AsText(items).Contains("To do:"), "a problem says what to do");
        }

        static void PolicyFiles()
        {
            string admx = File.ReadAllText("policy/PxeVice.admx");
            string adml = File.ReadAllText("policy/en-US/PxeVice.adml");
            foreach (string name in new string[] { Updater.PolicyCheck, Updater.PolicyInstall, PxePolicy.ReadOnlyName, PxePolicy.NoDeleteName, PxePolicy.RequireTicketName, PxePolicy.MaxMachinesName })
            {
                T.Check(admx.Contains("valueName=\"" + name + "\""), "the template has the value " + name);
                T.Check(adml.Contains("<string id=\"" + name + "\">") && adml.Contains("<string id=\"" + name + "_Help\">"), "the template has the text of " + name);
            }
            T.Check(admx.Contains("HKLM") || admx.Contains("Software\\Policies\\PxeVice"), "the template writes under Policies\\PxeVice");
            T.Equal(@"SOFTWARE\Policies\PxeVice", Updater.PolicyKey, "the key the program reads is the key the template writes");
        }
    }
}
