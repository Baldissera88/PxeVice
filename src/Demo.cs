namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Threading;

    // A site in memory with typical leftovers, for PxeVice.exe --demo, screenshots and the tests.
    public sealed class DemoSite : ISite
    {
        readonly SiteInfo info = new SiteInfo();
        public readonly List<PxeFlag> Flags = new List<PxeFlag>();
        public readonly List<DeviceRecord> Devices = new List<DeviceRecord>();
        public readonly List<string> Restarted = new List<string>();
        public readonly List<TsDeployment> Deployments = new List<TsDeployment>();
        public readonly Dictionary<string, List<uint>> Members = new Dictionary<string, List<uint>>();
        public readonly Dictionary<string, List<string>> Content = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<PxePoint> Points = new List<PxePoint>();
        // A PXE boot of a machine, written to SMSPXE.log when its time has come.
        sealed class Boot
        {
            public DateTime Utc;
            public string Mac, Guid;
            public int Arch = 9;
            // The PXE point that answered. Null is the first one.
            public string Server;
            public bool Written;
            public bool DuplicateSmbios;
            // Decided by what the site has at the time of the boot, unless set.
            public string Fixed;
        }
        readonly List<Boot> boots = new List<Boot>();
        int thread = 4100;
        // Seconds before a machine that was reset PXE boots again. 0 turns it off.
        public int NextBootSeconds = 8;
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
            info.PxeServers.Add("DP02.contoso.local");
            info.FoundPxeServers.AddRange(info.PxeServers);

            // A task sequence on an unknown computer failed before it got a name: a PXE flag and an Unknown record.
            Flag(0, "00:15:5D:23:21:3B", "74B241D3-5E0A-4380-B552-F350682ABD84", null, "P0120006", now.AddMinutes(-7));
            Device(16777334, "Unknown", "74B241D3-5E0A-4380-B552-F350682ABD84", now.AddMinutes(-1), false, "00:15:5D:23:21:3B", "00:15:5D:23:21:3C");
            // An unknown computer that was offered the required zero touch deployment.
            Flag(2046820353, "3C:52:82:4A:91:07", "4C4C4544-0052-3410-8048-B4C04F4A3132", null, "P0120009", now.AddMinutes(-35));
            // A device that was deleted from the console, but its PXE flag stayed.
            Flag(16777290, "98:FA:9B:12:44:C0", "4C4C4544-0039-4710-8031-C8C04F395A32", "PC-ACC-017", "P0120004", now.AddHours(-3));
            // A device in the console with a required deployment recorded.
            Flag(16777301, "F8:75:A4:0B:7E:21", "36A2E5C1-8F44-4D5B-9C1E-2B7A90D3E415", "PC-HR-042", "P0120008", now.AddHours(-26));
            Device(16777301, "PC-HR-042", "36A2E5C1-8F44-4D5B-9C1E-2B7A90D3E415", now.AddDays(-40), true, "F8:75:A4:0B:7E:21");
            // An Unknown record from an earlier failed build, without a PXE flag.
            Device(16777329, "Unknown", "9D1F7A62-3B0E-4C8A-A5D7-61E2F0B4C98D", now.AddDays(-2), false, "00:15:5D:23:21:40");
            // A USB network adapter that reports no SMBIOS GUID.
            Flag(2046820353, "00:E0:4C:68:01:1A", null, null, "P0120007", now.AddDays(-5));
            // Devices that have nothing to do with PXE.
            Device(16777288, "PC-IT-003", "1E9B2C44-7A10-4F6E-8D25-3C6A0B7E9F12", now.AddDays(-90), true, "A4:BB:6D:10:22:33");
            Device(16777212, "SRV-FILE01", "5B8E1D20-6C3F-4A77-B0E9-48D2C1F7A605", now.AddDays(-300), true, "00:50:56:9A:11:02");
            // A spare PC whose firmware reports the SMBIOS GUID many machines of this model have.
            Device(16777350, "PC-LAB-SPARE", "03000200-0400-0500-0006-000700080009", now.AddDays(-60), true, "D8:BB:C1:0F:33:01");
            // A kiosk PC in a collection whose deployment is for clients only.
            Device(16777360, "PC-KIOSK-07", "5D0E8B13-77A2-4C61-9F08-1B3C5E7D9A24", now.AddDays(-12), true, "70:B5:E8:21:9C:44");

            Collection("SMS000US", 2046820352, 2046820353);
            Collection("P0100021", 16777301);
            Collection("P0100022", 16777360);
            Deploy("P0120006", "Windows 11 24H2 Standard", "P0100010", "P0100005", "SMS000US", "All Unknown Computers", TsDeployment.FlagMediaAndPxe, false, null, null);
            Deploy("P0120009", "Windows 11 24H2 Zero Touch", "P0100013", "P0100005", "SMS000US", "All Unknown Computers", TsDeployment.FlagMediaAndPxe, true, null, null);
            Deploy("P0120008", "Windows 11 24H2 Standard", "P0100010", "P0100005", "P0100021", "OSD - HR", TsDeployment.FlagMediaAndPxe, true, null, null);
            Deploy("P0120004", "Windows 11 24H2 Kiosk", "P0100011", "P0100006", "P0100022", "OSD - Kiosk", TsDeployment.FlagHiddenInWinPe, false, null, null);
            Deploy("P0120007", "Windows 10 22H2 Legacy", "P0100012", "P0100005", "SMS000US", "All Unknown Computers", TsDeployment.FlagMediaAndPxe, false, null, new DateTime(2026, 6, 30, 22, 0, 0, DateTimeKind.Utc));
            Content["P0100005"] = new List<string>(new string[] { "DP01.contoso.local", "DP02.contoso.local" });
            Content["P0100006"] = new List<string>(new string[] { "DP02.contoso.local" });
            PxePoint pp = new PxePoint();
            pp.Server = "DP01.contoso.local";
            pp.SupportsUnknown = true;
            pp.Active = true;
            Points.Add(pp);
            PxePoint p2 = new PxePoint();
            p2.Server = "DP02.contoso.local";
            p2.SupportsUnknown = true;
            p2.Active = true;
            Points.Add(p2);

            // SMSPXE.log: what these machines got when they last PXE booted.
            Pxe(now.AddMinutes(-36), "3C:52:82:4A:91:07", "4C4C4544-0052-3410-8048-B4C04F4A3132", "offer:P0120009:1:2046820353");
            Pxe(now.AddMinutes(-8), "00:15:5D:23:21:3B", "74B241D3-5E0A-4380-B552-F350682ABD84", "offer:P0120006:0:2046820353");
            Pxe(now.AddMinutes(-0.5), "00:15:5D:23:21:3B", "74B241D3-5E0A-4380-B552-F350682ABD84", "none:16777334");
            Pxe(now.AddMinutes(-20), "F8:75:A4:0B:7E:21", "36A2E5C1-8F44-4D5B-9C1E-2B7A90D3E415", "none:16777301");
            Boot dup = Pxe(now.AddMinutes(-12), "D8:BB:C1:0F:33:72", "03000200-0400-0500-0006-000700080009", "none:16777350");
            dup.DuplicateSmbios = true;
            Pxe(now.AddMinutes(-50), "70:B5:E8:21:9C:44", "5D0E8B13-77A2-4C61-9F08-1B3C5E7D9A24", "none:16777360").Server = "DP02.contoso.local";
            Pxe(now.AddMinutes(-95), "9C:7B:EF:44:10:5A", "B91E0C22-5A47-4E88-A0C3-6D2F17E84B90", "offer:P0120006:0:2046820353");
            boots.Sort(delegate (Boot a, Boot b) { return a.Utc.CompareTo(b.Utc); });
        }

        Boot Pxe(DateTime utc, string mac, string guid, string fixedResult)
        {
            Boot b = new Boot();
            b.Utc = utc;
            b.Mac = mac;
            b.Guid = guid;
            b.Fixed = fixedResult;
            boots.Add(b);
            return b;
        }

        void Collection(string id, params uint[] members)
        {
            Members[id] = new List<uint>(members);
        }

        void Deploy(string id, string name, string pkg, string boot, string coll, string collName, uint flags, bool required, DateTime? from, DateTime? until)
        {
            TsDeployment d = new TsDeployment();
            d.Id = id;
            d.Name = name;
            d.PackageId = pkg;
            d.BootImageId = boot;
            d.CollectionId = coll;
            d.CollectionName = collName;
            d.Flags = flags;
            d.Required = required;
            d.AvailableUtc = from;
            d.ExpiresUtc = until;
            Deployments.Add(d);
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
            foreach (TsDeployment x in Deployments) d[x.Id] = x.Name;
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
        // After a reset the machine PXE boots again, the way a supporter would restart it.
        void Rebooted(string mac, string guid)
        {
            if (NextBootSeconds <= 0 || (mac == null && guid == null)) return;
            DateTime at = DateTime.UtcNow.AddSeconds(NextBootSeconds);
            lock (boots)
            {
                foreach (Boot x in boots) if (x.Utc > DateTime.UtcNow && x.Mac == mac) return;
                Boot b = new Boot();
                b.Utc = at;
                b.Mac = mac;
                b.Guid = guid;
                boots.Add(b);
            }
        }

        public void ClearFlags(uint[] ids)
        {
            Wait();
            foreach (DeviceRecord d in Devices) if (Array.IndexOf(ids, d.ResourceId) >= 0) Rebooted(d.MacAddresses.Count > 0 ? d.MacAddresses[0] : null, d.SmbiosGuid);
            Flags.RemoveAll(delegate (PxeFlag f) { return f.MachineId > 0 && f.MachineId <= uint.MaxValue && Array.IndexOf(ids, (uint)f.MachineId) >= 0; });
        }

        // Set to act like a site that has not let go of a deleted device yet.
        public bool KeepDeleted;

        public void DeleteDevice(uint id)
        {
            Wait();
            if (KeepDeleted) return;
            foreach (DeviceRecord d in Devices) if (d.ResourceId == id) Rebooted(d.MacAddresses.Count > 0 ? d.MacAddresses[0] : null, d.SmbiosGuid);
            int n = Devices.RemoveAll(delegate (DeviceRecord d) { return d.ResourceId == id; });
            if (n == 0) throw new InvalidOperationException("Device " + id + " was not found.");
        }

        public int DeleteFlags(string mac, string guid, uint[] ids)
        {
            Wait();
            Rebooted(mac, guid);
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

        public List<TsDeployment> TaskSequenceDeployments()
        {
            Wait();
            return new List<TsDeployment>(Deployments);
        }

        public List<string> CollectionsOf(uint resourceId)
        {
            List<string> l = new List<string>();
            foreach (KeyValuePair<string, List<uint>> kv in Members) if (kv.Value.Contains(resourceId)) l.Add(kv.Key);
            return l;
        }

        public List<string> ContentServers(string packageId)
        {
            List<string> l;
            return Content.TryGetValue(packageId, out l) ? new List<string>(l) : new List<string>();
        }

        public List<PxePoint> PxePoints()
        {
            return new List<PxePoint>(Points);
        }

        // What the PXE point answers now: the first deployment for PXE that reaches the machine and is not held back by a flag.
        string Decide(Boot b)
        {
            string guid = Ids.Guid(b.Guid), mac = Ids.Mac(b.Mac);
            List<DeviceRecord> devices = FindDevices(mac, guid, null, null);
            List<uint> targets = new List<uint>();
            long item;
            if (devices.Count > 0)
            {
                foreach (DeviceRecord d in devices) targets.Add(d.ResourceId);
                item = devices[0].ResourceId;
            }
            else
            {
                targets.Add(2046820353);
                item = 2046820353;
            }
            // Flags of an unknown computer are found by its MAC address and SMBIOS GUID, not by the Unknown Computer object.
            List<PxeFlag> flags = FlagsFor(mac, guid, devices.Count > 0 ? targets.ToArray() : null);
            foreach (TsDeployment d in Deployments)
            {
                bool reach = false;
                foreach (uint t in targets) if (Members.ContainsKey(d.CollectionId) && Members[d.CollectionId].Contains(t)) reach = true;
                if (!reach || !d.ForPxe || d.Expired(DateTime.UtcNow)) continue;
                if (d.Required && flags.Exists(delegate (PxeFlag f) { return f.DeploymentId == d.Id; })) continue;
                return "offer:" + d.Id + ":" + (d.Required ? "1" : "0") + ":" + item.ToString(CultureInfo.InvariantCulture);
            }
            return "none:" + item.ToString(CultureInfo.InvariantCulture);
        }

        static string Line(string text, DateTime utc, string thread)
        {
            DateTime local = utc.ToLocalTime();
            int bias = -(int)Math.Round(TimeZone.CurrentTimeZone.GetUtcOffset(local).TotalMinutes);
            return "<![LOG[" + text + "]LOG]!><time=\"" + local.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + (bias >= 0 ? "+" : "-") + Math.Abs(bias).ToString("000", CultureInfo.InvariantCulture) +
                "\" date=\"" + local.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture) + "\" component=\"SMSPXE\" context=\"\" type=\"1\" thread=\"" + thread + "\" file=\"database.cpp:752\">\r\n";
        }

        // The lines SMSPXE.log gets for one boot.
        string Write(Boot b)
        {
            string result = b.Fixed ?? Decide(b);
            string[] p = result.Split(':');
            bool offer = p[0] == "offer";
            string item = p[p.Length - 1];
            long key = long.Parse(item, CultureInfo.InvariantCulture);
            bool unknown = key >= Ids.UnknownComputerStart;
            int t = thread++;
            string th = t.ToString(CultureInfo.InvariantCulture) + " (0x" + t.ToString("X4", CultureInfo.InvariantCulture) + ")";
            string id = b.Mac + ", " + b.Guid + ": ";
            DateTime at = b.Utc;
            StringBuilder s = new StringBuilder();
            s.Append(Line("Prioritizing local MP http://CM01.contoso.local.", at, th));
            s.Append(Line("Request using architecture " + b.Arch.ToString(CultureInfo.InvariantCulture) + ".", at, th));
            s.Append(Line(id + "device is " + (unknown ? "not " : "") + "in the database.", at, th));
            string netbios = "";
            foreach (DeviceRecord d in Devices) if (d.ResourceId == key && !d.IsUnknownRecord) netbios = d.Name;
            string offerId = offer ? p[1] : "", pkg = "", bootImg = "", mand = offer ? p[2] : "";
            if (offer)
            {
                foreach (TsDeployment d in Deployments) if (d.Id == offerId) { pkg = d.PackageId; bootImg = d.BootImageId; }
            }
            s.Append(Line("Client boot action reply: <ClientIDReply><Identification Unknown=\"" + (unknown ? "1" : "0") + "\" DuplicateSMBIOS=\"" + (b.DuplicateSmbios ? "1" : "0") + "\" DuplicateMACAddress=\"0\" ItemKey=\"" + item + "\" ServerName=\"\"><Machine><ClientID/><NetbiosName>" + netbios + "</NetbiosName></Machine></Identification><PXEBootAction LastPXEAdvertisementID=\"\" LastPXEAdvertisementTime=\"\" OfferID=\"" + offerId + "\" OfferIDTime=\"\" PkgID=\"" + pkg + "\" PackageVersion=\"\" PackagePath=\"\" BootImageID=\"" + bootImg + "\" Mandatory=\"" + mand + "\"/></ClientIDReply>", at.AddMilliseconds(40), th));
            if (offer)
            {
                s.Append(Line(id + "found " + (mand == "1" ? "mandatory" : "optional") + " advertisement " + offerId, at.AddMilliseconds(60), th));
                s.Append(Line("Looking for bootImage " + bootImg, at.AddMilliseconds(80), th));
            }
            else
            {
                s.Append(Line(id + "no advertisements found", at.AddMilliseconds(60), th));
                s.Append(Line("Using Management Point: http://CM01.contoso.local", at.AddMilliseconds(70), th));
                s.Append(Line(id + "Not serviced.", at.AddMilliseconds(90), th));
            }
            return s.ToString();
        }

        public List<LogLine> ReadPxeLog(string server, out string note)
        {
            note = null;
            StringBuilder text = new StringBuilder();
            lock (boots)
            {
                boots.Sort(delegate (Boot a, Boot b) { return a.Utc.CompareTo(b.Utc); });
                foreach (Boot b in boots)
                {
                    if (b.Written || b.Utc > DateTime.UtcNow) continue;
                    if (!Checker.SameServer(b.Server ?? info.PxeServers[0], server)) continue;
                    b.Written = true;
                    text.Append(Write(b));
                }
            }
            int used, ln = 0;
            return CmLog.Parse(text.ToString(), @"\\" + server + @"\SMS_DP$\sms\logs\SMSPXE.log", ref ln, true, null, out used);
        }

        public void Dispose()
        {
        }
    }
}
