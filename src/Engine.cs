namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    public sealed class ResetOptions
    {
        // Device records named "Unknown" are leftovers from a task sequence that failed before the computer got a name.
        // While one exists, the machine is known and no longer gets deployments for Unknown Computers.
        public bool DeleteUnknownRecords = true;
        // Named device records are only deleted when asked for.
        public bool DeleteDevices;
        public bool RestartPxeService;
    }

    // What PxeVice read from the site.
    public sealed class SiteData
    {
        public List<PxeFlag> Flags = new List<PxeFlag>();
        public List<DeviceRecord> UnknownRecords = new List<DeviceRecord>();
        public List<Machine> Machines = new List<Machine>();
        public Dictionary<string, string> DeploymentNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public DateTime ReadUtc;
    }

    public static class Engine
    {
        // More rows than this for one machine means the match is too wide, for example a MAC address without a GUID
        // on a network adapter that many machines share. PxeVice does not delete them.
        public const int MaxRowsPerMachine = 25;

        public static SiteData Read(ISite site)
        {
            SiteData d = new SiteData();
            d.Flags = site.ReadFlags();
            d.UnknownRecords = site.UnknownRecords();
            List<string> ids = new List<string>();
            foreach (PxeFlag f in d.Flags) if (!string.IsNullOrEmpty(f.DeploymentId) && !ids.Contains(f.DeploymentId)) ids.Add(f.DeploymentId);
            d.DeploymentNames = site.DeploymentNames(ids);
            d.Machines = Merge(d.Flags, d.UnknownRecords);
            foreach (Machine m in d.Machines) m.DeploymentNames = d.DeploymentNames;
            d.ReadUtc = DateTime.UtcNow;
            return d;
        }

        public static string Key(string mac, string guid)
        {
            if (guid != null) return "guid:" + guid;
            if (mac != null) return "mac:" + mac;
            return null;
        }

        // One machine per SMBIOS GUID, or per MAC address when there is no GUID. A record with neither stays on its own.
        public static List<Machine> Merge(List<PxeFlag> flags, List<DeviceRecord> devices)
        {
            Dictionary<string, Machine> byKey = new Dictionary<string, Machine>(StringComparer.OrdinalIgnoreCase);
            List<Machine> list = new List<Machine>();
            int loose = 0;
            Func<string, string, Machine> get = delegate (string mac, string guid)
            {
                string k = Key(mac, guid);
                Machine m;
                if (k != null && byKey.TryGetValue(k, out m))
                {
                    if (m.MacAddress == null) m.MacAddress = mac;
                    return m;
                }
                // A record with only a MAC address joins a machine that has that MAC address and a GUID.
                if (guid == null && mac != null)
                {
                    foreach (Machine x in list) if (x.SmbiosGuid != null && x.AllMacs.Contains(mac)) return x;
                }
                m = new Machine();
                m.Key = k ?? "none:" + (loose++).ToString(CultureInfo.InvariantCulture);
                m.MacAddress = mac;
                m.SmbiosGuid = guid;
                byKey[m.Key] = m;
                list.Add(m);
                return m;
            };
            foreach (PxeFlag f in flags) get(f.MacAddress, f.SmbiosGuid).Flags.Add(f);
            foreach (DeviceRecord d in devices)
            {
                Machine m = get(d.MacAddresses.Count > 0 ? d.MacAddresses[0] : null, d.SmbiosGuid);
                bool have = false;
                foreach (DeviceRecord x in m.Devices) if (x.ResourceId == d.ResourceId) have = true;
                if (!have) m.Devices.Add(d);
            }
            list.Sort(delegate (Machine a, Machine b)
            {
                DateTime x = a.LastSeenUtc ?? DateTime.MinValue, y = b.LastSeenUtc ?? DateTime.MinValue;
                return y.CompareTo(x);
            });
            return list;
        }

        // Reads the site again for each machine, so the plan matches what is there right now.
        public static Plan BuildPlan(ISite site, IList<Machine> machines, ResetOptions o)
        {
            Plan p = new Plan();
            foreach (Machine m in machines)
            {
                string mac = m.MacAddress, guid = m.SmbiosGuid;
                List<uint> known = new List<uint>();
                foreach (DeviceRecord d in m.Devices) known.Add(d.ResourceId);
                // Flags of real devices, including deleted ones, are also matched by their resource ID.
                List<uint> flagIds = new List<uint>();
                foreach (PxeFlag f in m.Flags)
                {
                    if (f.IsUnknownComputer || f.MachineId > uint.MaxValue) continue;
                    uint id = (uint)f.MachineId;
                    if (!flagIds.Contains(id)) flagIds.Add(id);
                    if (!known.Contains(id)) known.Add(id);
                }
                List<DeviceRecord> devices = site.FindDevices(mac, guid, null, known.ToArray());
                // Without an SMBIOS GUID, a MAC address can belong to a network adapter that several devices have used.
                // Named devices found only by that MAC address are then left alone when there is more than one.
                if (guid == null && mac != null)
                {
                    List<DeviceRecord> byMacOnly = devices.FindAll(delegate (DeviceRecord d) { return !d.IsUnknownRecord && !known.Contains(d.ResourceId); });
                    if (byMacOnly.Count > 1)
                    {
                        devices.RemoveAll(delegate (DeviceRecord d) { return byMacOnly.Contains(d); });
                        p.Notes.Add(m.Name + ": " + byMacOnly.Count + " devices (" + Names(byMacOnly) + ") have used MAC address " + mac + ", probably a shared network adapter, so they are left alone. Reset a device by its name to clear its flag.");
                    }
                }
                List<uint> ids = new List<uint>();
                foreach (DeviceRecord d in devices) ids.Add(d.ResourceId);
                List<uint> rowIds = new List<uint>(ids);
                foreach (uint id in flagIds) if (!rowIds.Contains(id)) rowIds.Add(id);
                if (mac == null && guid == null && rowIds.Count == 0)
                {
                    p.Notes.Add(m.Name + ": no MAC address, SMBIOS GUID or device record to match on, so nothing is changed.");
                    continue;
                }
                string label = m.Name + Describe(mac, guid);
                if (ids.Count > 0)
                {
                    Step clear = new Step();
                    clear.Kind = StepKind.ClearFlag;
                    clear.MachineName = m.Name;
                    clear.ResourceIds = ids.ToArray();
                    clear.Target = Names(devices);
                    p.Steps.Add(clear);
                }
                foreach (DeviceRecord d in devices)
                {
                    StepKind k;
                    if (d.IsUnknownRecord)
                    {
                        if (!o.DeleteUnknownRecords) continue;
                        k = StepKind.DeleteUnknownRecord;
                    }
                    else
                    {
                        if (!o.DeleteDevices) continue;
                        k = StepKind.DeleteDevice;
                    }
                    Step s = new Step();
                    s.Kind = k;
                    s.MachineName = m.Name;
                    s.ResourceIds = new uint[] { d.ResourceId };
                    s.Target = d.Name + " (" + d.ResourceId.ToString(CultureInfo.InvariantCulture) + ")";
                    p.Steps.Add(s);
                }
                // Clear Required PXE Deployments removes the rows of the devices it clears. The rest is deleted from the table.
                List<PxeFlag> rows = new List<PxeFlag>();
                foreach (PxeFlag f in site.FlagsFor(mac, guid, rowIds.ToArray()))
                {
                    if (!f.IsUnknownComputer && f.MachineId <= uint.MaxValue && ids.Contains((uint)f.MachineId)) continue;
                    rows.Add(f);
                }
                if (rows.Count > MaxRowsPerMachine)
                {
                    p.Notes.Add(label + ": " + rows.Count + " PXE flag rows match, more than " + MaxRowsPerMachine + ". That usually means a network adapter that many machines share, so they are left alone. Reset the machine by its SMBIOS GUID instead.");
                }
                else if (rows.Count > 0)
                {
                    Step s = new Step();
                    s.Kind = StepKind.DeleteFlagRows;
                    s.MachineName = m.Name;
                    s.MacAddress = mac;
                    s.SmbiosGuid = guid;
                    s.ResourceIds = rowIds.ToArray();
                    s.Rows = rows.Count;
                    s.Target = label.Trim();
                    p.Steps.Add(s);
                }
                if (ids.Count == 0 && rows.Count == 0) p.Notes.Add(label + ": nothing is left for this machine.");
            }
            if (o.RestartPxeService && p.Steps.Count > 0)
            {
                foreach (string server in site.Info.PxeServers)
                {
                    List<string> running;
                    try { running = site.RunningPxeServices(server); }
                    catch (Exception ex)
                    {
                        p.Notes.Add("The PXE service on " + server + " could not be read: " + ex.Message);
                        continue;
                    }
                    if (running.Count == 0) p.Notes.Add("No running WDSServer or SccmPxe service on " + server + ".");
                    foreach (string svc in running)
                    {
                        Step s = new Step();
                        s.Kind = StepKind.RestartService;
                        s.Server = server;
                        s.Service = svc;
                        s.Target = svc + " on " + server;
                        p.Steps.Add(s);
                    }
                }
                if (site.Info.PxeServers.Count == 0) p.Notes.Add("No PXE distribution point is known, so no service is restarted. Add one in Settings.");
            }
            return p;
        }

        static string Describe(string mac, string guid)
        {
            List<string> parts = new List<string>();
            if (mac != null) parts.Add(mac);
            if (guid != null) parts.Add(guid);
            return parts.Count == 0 ? "" : " (" + string.Join(", ", parts.ToArray()) + ")";
        }

        static string Names(List<DeviceRecord> devices)
        {
            List<string> parts = new List<string>();
            foreach (DeviceRecord d in devices) parts.Add(d.Name + " (" + d.ResourceId.ToString(CultureInfo.InvariantCulture) + ")");
            return string.Join(", ", parts.ToArray());
        }

        // Runs the steps in order. A step that fails is reported and the next one still runs.
        public static List<Activity> Execute(ISite site, Plan plan, Action<Activity> progress)
        {
            List<Activity> done = new List<Activity>();
            string user = Environment.UserDomainName + "\\" + Environment.UserName;
            foreach (Step s in plan.Steps)
            {
                Activity a = new Activity();
                a.TimeUtc = DateTime.UtcNow;
                a.Machine = s.MachineName ?? s.Server;
                a.Action = s.Text;
                a.User = user;
                try
                {
                    switch (s.Kind)
                    {
                        case StepKind.ClearFlag:
                            site.ClearFlags(s.ResourceIds);
                            a.Result = "Cleared";
                            break;
                        case StepKind.DeleteUnknownRecord:
                        case StepKind.DeleteDevice:
                            site.DeleteDevice(s.ResourceIds[0]);
                            a.Result = "Deleted";
                            break;
                        case StepKind.DeleteFlagRows:
                            int n = site.DeleteFlags(s.MacAddress, s.SmbiosGuid, s.ResourceIds);
                            a.Result = n == 0 ? "Nothing left to delete" : n + (n == 1 ? " row deleted" : " rows deleted");
                            break;
                        case StepKind.RestartService:
                            site.RestartService(s.Server, s.Service);
                            a.Result = "Restarted";
                            break;
                    }
                    a.Ok = true;
                }
                catch (Exception ex)
                {
                    a.Ok = false;
                    a.Result = ex.Message;
                }
                done.Add(a);
                ActivityLog.Write(a);
                if (progress != null) progress(a);
            }
            return done;
        }
    }

    // Every change PxeVice makes is written to %LOCALAPPDATA%\PxeVice\PxeVice.log in the CMTrace format, so LogVice and CMTrace can open it.
    public static class ActivityLog
    {
        static readonly object gate = new object();
        public static bool Enabled = true;

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PxeVice"); }
        }

        public static string FilePath
        {
            get { return Path.Combine(Folder, "PxeVice.log"); }
        }

        public static string Line(Activity a)
        {
            DateTime local = a.TimeUtc.ToLocalTime();
            int bias = -(int)Math.Round(TimeZone.CurrentTimeZone.GetUtcOffset(local).TotalMinutes);
            string text = (a.Machine ?? "") + ": " + a.Action + ". " + (a.Ok ? "" : "Failed: ") + a.Result + " (by " + a.User + ")";
            return "<![LOG[" + text.Replace("]LOG]!>", "]LOG]") + "]LOG]!>" +
                "<time=\"" + local.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + (bias >= 0 ? "+" : "-") + Math.Abs(bias).ToString("000", CultureInfo.InvariantCulture) + "\" " +
                "date=\"" + local.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture) + "\" component=\"PxeVice\" context=\"\" type=\"" + (a.Ok ? "1" : "3") + "\" " +
                "thread=\"" + System.Threading.Thread.CurrentThread.ManagedThreadId.ToString(CultureInfo.InvariantCulture) + "\" file=\"\">";
        }

        public static void Write(Activity a)
        {
            if (!Enabled) return;
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Folder);
                    // Keep one older log, like CMTrace logs do.
                    FileInfo fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                    {
                        string old = Path.Combine(Folder, "PxeVice.lo_");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, Line(a) + "\r\n", new UTF8Encoding(false));
                }
            }
            catch (Exception) { }
        }
    }
}
