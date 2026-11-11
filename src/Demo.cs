namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    // A site in memory with typical leftovers, for PxeVice.exe --demo, screenshots and the tests.
    public sealed class DemoSite : ISite
    {
        readonly SiteInfo info = new SiteInfo();
        public readonly List<PxeFlag> Flags = new List<PxeFlag>();
        public readonly List<DeviceRecord> Devices = new List<DeviceRecord>();
        public readonly List<string> Restarted = new List<string>();
        public int Delay;

        public DemoSite() : this(DateTime.UtcNow) { }

        public DemoSite(DateTime now)
        {
            info.Demo = true;
            info.SiteServer = "CM01.contoso.local";
            info.Provider = "CM01.contoso.local";
            info.SiteCode = "P01";
            info.SqlServer = "CM01.contoso.local";
            info.Database = "CM_P01";
            info.PxeServers.Add("DP01.contoso.local");

            // A task sequence on an unknown computer failed before it got a name: a PXE flag and an Unknown record.
            Flag(0, "00:15:5D:23:21:3B", "74B241D3-5E0A-4380-B552-F350682ABD84", null, "P0120006", now.AddMinutes(-7));
            Device(16777334, "Unknown", "74B241D3-5E0A-4380-B552-F350682ABD84", now.AddMinutes(-1), false, "00:15:5D:23:21:3B", "00:15:5D:23:21:3C");
            // An unknown computer that ran a required deployment and now gets "no advertisements found".
            Flag(2046820353, "3C:52:82:4A:91:07", "4C4C4544-0052-3410-8048-B4C04F4A3132", null, "P0120006", now.AddMinutes(-35));
            // A device that was deleted from the console, but its PXE flag stayed.
            Flag(16777290, "98:FA:9B:12:44:C0", "4C4C4544-0039-4710-8031-C8C04F395A32", "PC-ACC-017", "P0120004", now.AddHours(-3));
            // A device in the console with a required deployment recorded.
            Flag(16777301, "F8:75:A4:0B:7E:21", "36A2E5C1-8F44-4D5B-9C1E-2B7A90D3E415", "PC-HR-042", "P0120006", now.AddHours(-26));
            Device(16777301, "PC-HR-042", "36A2E5C1-8F44-4D5B-9C1E-2B7A90D3E415", now.AddDays(-40), true, "F8:75:A4:0B:7E:21");
            // An Unknown record from an earlier failed build, without a PXE flag.
            Device(16777329, "Unknown", "9D1F7A62-3B0E-4C8A-A5D7-61E2F0B4C98D", now.AddDays(-2), false, "00:15:5D:23:21:40");
            // A USB network adapter that reports no SMBIOS GUID.
            Flag(2046820353, "00:E0:4C:68:01:1A", null, null, "P0120007", now.AddDays(-5));
            // Devices that have nothing to do with PXE.
            Device(16777288, "PC-IT-003", "1E9B2C44-7A10-4F6E-8D25-3C6A0B7E9F12", now.AddDays(-90), true, "A4:BB:6D:10:22:33");
            Device(16777212, "SRV-FILE01", "5B8E1D20-6C3F-4A77-B0E9-48D2C1F7A605", now.AddDays(-300), true, "00:50:56:9A:11:02");
        }

        void Flag(long id, string mac, string guid, string netbios, string deployment, DateTime utc)
        {
            PxeFlag f = new PxeFlag();
            f.MachineId = id;
            f.RawMac = mac;
            f.MacAddress = Ids.Mac(mac);
            f.SmbiosGuid = Ids.Guid(guid);
            f.Netbios = netbios;
            f.DeploymentId = deployment;
            f.TimeUtc = utc;
            f.SiteCode = "P01";
            Flags.Add(f);
        }

        void Device(uint id, string name, string guid, DateTime created, bool client, params string[] macs)
        {
            DeviceRecord d = new DeviceRecord();
            d.ResourceId = id;
            d.Name = name;
            d.SmbiosGuid = Ids.Guid(guid);
            d.CreatedUtc = created;
            d.Client = client;
            foreach (string m in macs) d.MacAddresses.Add(Ids.Mac(m));
            Devices.Add(d);
        }

        public SiteInfo Info
        {
            get { return info; }
        }

        void Wait()
        {
            if (Delay > 0) Thread.Sleep(Delay);
        }

        // Like the view join in LiveSite: the name of the device a flag belongs to, when it exists.
        PxeFlag WithName(PxeFlag f)
        {
            f.DeviceName = null;
            foreach (DeviceRecord d in Devices) if (d.ResourceId == f.MachineId) f.DeviceName = d.Name;
            return f;
        }

        public List<PxeFlag> ReadFlags()
        {
            Wait();
            List<PxeFlag> list = new List<PxeFlag>();
            foreach (PxeFlag f in Flags) list.Add(WithName(f));
            return list;
        }

        public List<PxeFlag> FlagsFor(string mac, string guid, uint[] ids)
        {
            List<PxeFlag> list = new List<PxeFlag>();
            foreach (PxeFlag f in Flags) if (Matching.Flag(f, mac, guid, ids)) list.Add(WithName(f));
            return list;
        }

        public Dictionary<string, string> DeploymentNames(ICollection<string> ids)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            d["P0120004"] = "Windows 11 24H2 Kiosk";
            d["P0120006"] = "Windows 11 24H2 Standard";
            d["P0120007"] = "Windows 10 22H2 Legacy";
            return d;
        }

        public List<DeviceRecord> UnknownRecords()
        {
            List<DeviceRecord> list = new List<DeviceRecord>();
            foreach (DeviceRecord d in Devices) if (d.IsUnknownRecord) list.Add(d);
            return list;
        }

        public List<DeviceRecord> FindDevices(string mac, string guid, string name, uint[] ids)
        {
            List<DeviceRecord> list = new List<DeviceRecord>();
            foreach (DeviceRecord d in Devices) if (Matching.Device(d, mac, guid, name, ids)) list.Add(d);
            return list;
        }

        // Clear Required PXE Deployments removes the flags recorded for these resource IDs. Flags recorded for
        // Unknown Computer objects stay, which is why PxeVice also deletes the rows.
        public void ClearFlags(uint[] ids)
        {
            Wait();
            Flags.RemoveAll(delegate (PxeFlag f) { return f.MachineId > 0 && f.MachineId <= uint.MaxValue && Array.IndexOf(ids, (uint)f.MachineId) >= 0; });
        }

        public void DeleteDevice(uint id)
        {
            Wait();
            int n = Devices.RemoveAll(delegate (DeviceRecord d) { return d.ResourceId == id; });
            if (n == 0) throw new InvalidOperationException("Device " + id + " was not found.");
        }

        public int DeleteFlags(string mac, string guid, uint[] ids)
        {
            Wait();
            return Flags.RemoveAll(delegate (PxeFlag f) { return Matching.Flag(f, mac, guid, ids); });
        }

        public List<string> RunningPxeServices(string server)
        {
            return new List<string>(new string[] { "WDSServer" });
        }

        public void RestartService(string server, string service)
        {
            Wait();
            Restarted.Add(service + "@" + server);
        }

        public string PxeLog(string server)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }
}
