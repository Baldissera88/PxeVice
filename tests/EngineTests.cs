namespace PxeVice.Tests
{
    using System;
    using System.Collections.Generic;

    public static class EngineTests
    {
        static readonly DateTime Now = new DateTime(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc);

        public static void Run()
        {
            Normalize();
            MergeOneLinePerMachine();
            ResetUnknownWithRecord();
            ResetKeepsNamedDevice();
            ResetDeletedDevice();
            SharedAdapterIsSafe();
            RestartService();
            NothingLeft();
            CliTarget();
            LogLine();
            Wql();
        }

        static Machine Find(List<Machine> list, string mac)
        {
            foreach (Machine m in list) if (m.AllMacs.Contains(mac)) return m;
            return null;
        }

        static void Normalize()
        {
            T.Equal("00:15:5D:23:21:3B", Ids.Mac("00-15-5d-23-21-3b"), "MAC with dashes");
            T.Equal("00:15:5D:23:21:3B", Ids.Mac("00155D23213B"), "MAC without separators");
            T.Equal("00:15:5D:23:21:3B", Ids.Mac("  00:15:5D:23:21:3B, 00:15:5D:23:21:3C"), "first of two MACs");
            T.Equal(2, Ids.Macs("00:15:5D:23:21:3B, 00:15:5D:23:21:3C").Count, "two MACs");
            T.Equal(null, Ids.Mac("no mac here"), "no MAC");
            T.Equal("74B241D3-5E0A-4380-B552-F350682ABD84", Ids.Guid("{74b241d3-5e0a-4380-b552-f350682abd84}"), "GUID with braces");
            T.Equal(null, Ids.Guid("00000000-0000-0000-0000-000000000000"), "empty GUID");
            T.Equal(null, Ids.Guid("not a guid"), "not a GUID");
        }

        static void MergeOneLinePerMachine()
        {
            DemoSite site = new DemoSite(Now);
            SiteData d = Engine.Read(site);
            Machine m = Find(d.Machines, "00:15:5D:23:21:3B");
            T.Check(m != null, "the lab machine is listed");
            if (m == null) return;
            T.Equal(1, m.Flags.Count, "lab machine: one flag");
            T.Equal(1, m.Devices.Count, "lab machine: one Unknown record");
            T.Equal("PXE flag + Unknown record", m.Found, "lab machine: found");
            T.Equal(MachineKinds.UnknownRecord, m.Kind, "lab machine: kind");
            T.Equal("P0120006", m.Deployments, "lab machine: deployment");
            T.Equal("P0120006 (Windows 11 24H2 Standard)", m.DeploymentText("P0120006"), "deployment name");
            T.Equal(m, d.Machines[0], "newest machine first");

            int machines = 0;
            foreach (Machine x in d.Machines) if (x.AllMacs.Contains("00:15:5D:23:21:3B")) machines++;
            T.Equal(1, machines, "flag and record merged into one line");

            Machine deleted = Find(d.Machines, "98:FA:9B:12:44:C0");
            T.Equal(MachineKinds.Deleted, deleted.Kind, "deleted device kind");
            T.Equal("PC-ACC-017", deleted.Name, "deleted device keeps its NetBIOS name");

            Machine hr = Find(d.Machines, "F8:75:A4:0B:7E:21");
            T.Equal(MachineKinds.Device, hr.Kind, "named device kind");
            T.Equal("PC-HR-042", hr.Name, "named device name");

            Machine usb = Find(d.Machines, "00:E0:4C:68:01:1A");
            T.Equal(MachineKinds.Unknown, usb.Kind, "unknown computer kind");
            T.Equal(null, usb.SmbiosGuid, "USB adapter without GUID");

            T.Equal(6, d.Machines.Count, "six machines in the demo");
        }

        static void ResetUnknownWithRecord()
        {
            DemoSite site = new DemoSite(Now);
            SiteData d = Engine.Read(site);
            Machine m = Find(d.Machines, "00:15:5D:23:21:3B");
            Plan p = Engine.BuildPlan(site, new List<Machine>(new Machine[] { m }), new ResetOptions());
            T.Equal(3, p.Steps.Count, "lab machine: three steps");
            T.Equal(StepKind.ClearFlag, p.Steps[0].Kind, "first clear the flag");
            T.Equal(StepKind.DeleteUnknownRecord, p.Steps[1].Kind, "then delete the Unknown record");
            T.Equal(StepKind.DeleteFlagRows, p.Steps[2].Kind, "then delete the rows");
            T.Equal(1, p.Steps[2].Rows, "one row");
            List<Activity> done = Engine.Execute(site, p, null);
            foreach (Activity a in done) T.Check(a.Ok, "step ok: " + a.Action + " " + a.Result);
            T.Equal(0, site.FlagsFor("00:15:5D:23:21:3B", "74B241D3-5E0A-4380-B552-F350682ABD84", null).Count, "lab machine: no flags left");
            T.Equal(0, site.FindDevices("00:15:5D:23:21:3B", "74B241D3-5E0A-4380-B552-F350682ABD84", null, null).Count, "lab machine: no records left");
            T.Equal(4, site.Flags.Count, "the other flags stay");
            T.Equal(4, site.Devices.Count, "the other devices stay");

            ResetOptions keep = new ResetOptions();
            keep.DeleteUnknownRecords = false;
            DemoSite site2 = new DemoSite(Now);
            Machine m2 = Find(Engine.Read(site2).Machines, "00:15:5D:23:21:3B");
            Plan p2 = Engine.BuildPlan(site2, new List<Machine>(new Machine[] { m2 }), keep);
            T.Equal(0, p2.Count(StepKind.DeleteUnknownRecord), "Unknown record kept when asked");
        }

        static void ResetKeepsNamedDevice()
        {
            DemoSite site = new DemoSite(Now);
            Machine m = Find(Engine.Read(site).Machines, "F8:75:A4:0B:7E:21");
            Plan p = Engine.BuildPlan(site, new List<Machine>(new Machine[] { m }), new ResetOptions());
            T.Equal(0, p.Count(StepKind.DeleteDevice), "named device not deleted by default");
            T.Equal(1, p.Count(StepKind.ClearFlag), "named device flag cleared");
            T.Equal(16777301u, p.Steps[0].ResourceIds[0], "cleared by resource ID");
            T.Equal(0, p.Count(StepKind.DeleteFlagRows), "named device: the clear removes its row, so no table step");
            Engine.Execute(site, p, null);
            T.Equal(0, site.FlagsFor(null, null, new uint[] { 16777301 }).Count, "named device flag gone");
            T.Equal(1, site.FindDevices(null, null, "PC-HR-042", null).Count, "named device stays");

            ResetOptions all = new ResetOptions();
            all.DeleteDevices = true;
            DemoSite site2 = new DemoSite(Now);
            Machine m2 = Find(Engine.Read(site2).Machines, "F8:75:A4:0B:7E:21");
            T.Equal(1, Engine.BuildPlan(site2, new List<Machine>(new Machine[] { m2 }), all).Count(StepKind.DeleteDevice), "named device deleted when asked");
        }

        static void ResetDeletedDevice()
        {
            DemoSite site = new DemoSite(Now);
            Machine m = Find(Engine.Read(site).Machines, "98:FA:9B:12:44:C0");
            Plan p = Engine.BuildPlan(site, new List<Machine>(new Machine[] { m }), new ResetOptions());
            T.Equal(0, p.Count(StepKind.ClearFlag), "deleted device: nothing to clear in the console");
            T.Equal(1, p.Count(StepKind.DeleteFlagRows), "deleted device: row deleted");
            Engine.Execute(site, p, null);
            T.Equal(0, site.FlagsFor(null, null, new uint[] { 16777290 }).Count, "deleted device flag gone");
        }

        static void SharedAdapterIsSafe()
        {
            DemoSite site = new DemoSite(Now);
            // Another machine that boots with the same USB adapter, and has an SMBIOS GUID.
            PxeFlag other = new PxeFlag();
            other.MachineId = 2046820353;
            other.RawMac = "00:E0:4C:68:01:1A";
            other.MacAddress = "00:E0:4C:68:01:1A";
            other.SmbiosGuid = "11111111-2222-3333-4444-555555555555";
            site.Flags.Add(other);
            int before = site.Flags.Count;
            // Resetting a machine by its GUID leaves the other machine's flag alone, even with the same MAC address.
            Machine m = new Machine();
            m.MacAddress = "00:E0:4C:68:01:1A";
            m.SmbiosGuid = "4C4C4544-0052-3410-8048-B4C04F4A3132";
            Plan p = Engine.BuildPlan(site, new List<Machine>(new Machine[] { m }), new ResetOptions());
            Engine.Execute(site, p, null);
            T.Check(site.Flags.Contains(other), "a flag with another GUID stays");
            T.Equal(before - 2, site.Flags.Count, "the machine's own flag and the flag without GUID are removed");

            // Without a GUID, named devices that only share the MAC address are left alone.
            DemoSite dongle = new DemoSite(Now);
            foreach (string n in new string[] { "PC-A", "PC-B" })
            {
                DeviceRecord d = new DeviceRecord();
                d.ResourceId = n == "PC-A" ? 16777401u : 16777402u;
                d.Name = n;
                d.MacAddresses.Add("00:E0:4C:68:01:1A");
                dongle.Devices.Add(d);
            }
            Machine u = new Machine();
            u.MacAddress = "00:E0:4C:68:01:1A";
            Plan pd = Engine.BuildPlan(dongle, new List<Machine>(new Machine[] { u }), new ResetOptions());
            T.Equal(0, pd.Count(StepKind.ClearFlag), "shared adapter: other devices not cleared");
            T.Check(pd.Notes.Count > 0 && pd.Notes[0].Contains("left alone"), "shared adapter: explained");
            T.Equal(1, pd.Count(StepKind.DeleteFlagRows), "shared adapter: the machine's own row is still deleted");

            // Too many rows for one machine: nothing is deleted.
            DemoSite crowded = new DemoSite(Now);
            for (int i = 0; i < Engine.MaxRowsPerMachine + 1; i++)
            {
                PxeFlag f = new PxeFlag();
                f.MachineId = 2046820353;
                f.RawMac = "00:E0:4C:68:01:1A";
                f.MacAddress = "00:E0:4C:68:01:1A";
                crowded.Flags.Add(f);
            }
            Machine usb = new Machine();
            usb.MacAddress = "00:E0:4C:68:01:1A";
            Plan pc = Engine.BuildPlan(crowded, new List<Machine>(new Machine[] { usb }), new ResetOptions());
            T.Equal(0, pc.Count(StepKind.DeleteFlagRows), "too many rows: not deleted");
            T.Check(pc.Notes.Count > 0 && pc.Notes[0].Contains("share"), "too many rows: explained");
        }

        static void RestartService()
        {
            DemoSite site = new DemoSite(Now);
            Machine m = Find(Engine.Read(site).Machines, "3C:52:82:4A:91:07");
            ResetOptions o = new ResetOptions();
            o.RestartPxeService = true;
            Plan p = Engine.BuildPlan(site, new List<Machine>(new Machine[] { m }), o);
            T.Equal(1, p.Count(StepKind.RestartService), "restart step added");
            T.Equal(StepKind.RestartService, p.Steps[p.Steps.Count - 1].Kind, "restart is last");
            Engine.Execute(site, p, null);
            T.Equal(1, site.Restarted.Count, "service restarted");
        }

        static void NothingLeft()
        {
            DemoSite site = new DemoSite(Now);
            Machine m = new Machine();
            m.MacAddress = "AA:BB:CC:DD:EE:FF";
            Plan p = Engine.BuildPlan(site, new List<Machine>(new Machine[] { m }), new ResetOptions());
            T.Equal(0, p.Steps.Count, "unknown MAC: no steps");
            T.Check(p.Notes.Count == 1 && p.Notes[0].Contains("nothing is left"), "unknown MAC: explained");
            ResetOptions o = new ResetOptions();
            o.RestartPxeService = true;
            T.Equal(0, Engine.BuildPlan(site, new List<Machine>(new Machine[] { m }), o).Count(StepKind.RestartService), "no restart when nothing changes");
        }

        static void CliTarget()
        {
            DemoSite site = new DemoSite(Now);
            Machine m = Cli.Target(site, null, null, "PC-HR-042");
            T.Check(m != null, "target by name");
            T.Equal("36A2E5C1-8F44-4D5B-9C1E-2B7A90D3E415", m.SmbiosGuid, "target by name finds the GUID");
            T.Equal(1, m.Flags.Count, "target by name finds the flag");
            T.Equal(null, Cli.Target(site, null, null, "NO-SUCH-PC"), "unknown name");
            Machine lab = Cli.Target(site, "00:15:5D:23:21:3B", null, null);
            T.Equal("74B241D3-5E0A-4380-B552-F350682ABD84", lab.SmbiosGuid, "target by MAC picks up the GUID");
            T.Equal("Unknown", lab.Name, "target by MAC finds the Unknown record");
            Machine byMac = Cli.Target(site, "98:FA:9B:12:44:C0", null, null);
            Plan p = Engine.BuildPlan(site, new List<Machine>(new Machine[] { byMac }), new ResetOptions());
            T.Equal(1, p.Count(StepKind.DeleteFlagRows), "target by MAC finds the deleted device's flag");
        }

        static void LogLine()
        {
            Activity a = new Activity();
            a.TimeUtc = Now;
            a.Machine = "Unknown";
            a.Action = "Delete the leftover device record Unknown (16777334)";
            a.Ok = true;
            a.Result = "Deleted";
            a.User = "CONTOSO\\cm_admin";
            string line = ActivityLog.Line(a);
            T.Check(line.StartsWith("<![LOG[Unknown: Delete the leftover device record Unknown (16777334). Deleted (by CONTOSO\\cm_admin)]LOG]!><time=\""), "CMTrace line: " + line);
            T.Check(line.Contains("component=\"PxeVice\"") && line.Contains("type=\"1\""), "CMTrace fields");
            a.Ok = false;
            T.Check(ActivityLog.Line(a).Contains("type=\"3\""), "failed step is an error line");
        }

        static void Wql()
        {
            T.Equal("O\\'Brien", LiveSite.Wql("O'Brien"), "WQL quote");
            string w = Matching.DeviceWql("00:15:5D:23:21:3B", "74B241D3-5E0A-4380-B552-F350682ABD84", null, new uint[] { 16777334 });
            T.Equal("SMBIOSGUID = '74B241D3-5E0A-4380-B552-F350682ABD84' OR (MACAddresses = '00:15:5D:23:21:3B' AND SMBIOSGUID IS NULL) OR ResourceId = 16777334", w, "device WQL");
            T.Equal(null, Matching.DeviceWql(null, null, null, null), "no criteria");
        }
    }
}
