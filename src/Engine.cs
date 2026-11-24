namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Threading;

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
        // Every PXE request in the lines read from SMSPXE.log, newest first.
        public List<PxeRequest> Requests = new List<PxeRequest>();
        public readonly List<string> Notes = new List<string>();
        public DateTime ReadUtc;
    }

    public static class Engine
    {
        // More rows than this for one machine means the match is too wide, for example a MAC address without a GUID
        // on a network adapter that many machines share. PxeVice does not delete them.
        public const int MaxRowsPerMachine = 25;

        // Machines with only PXE requests are listed while their last request is this recent and was not serviced.
        public static TimeSpan RequestWindow = TimeSpan.FromHours(24);

        public static SiteData Read(ISite site)
        {
            return Read(site, null);
        }

        // With a store, the new lines of SMSPXE.log on every PXE distribution point are read into it as well.
        public static SiteData Read(ISite site, PxeLogStore store)
        {
            SiteData d = new SiteData();
            d.Flags = site.ReadFlags();
            d.UnknownRecords = site.UnknownRecords();
            if (store != null)
            {
                ReadLogs(site, store);
                d.Requests = store.Requests();
                foreach (string n in store.NoteList()) d.Notes.Add(n);
            }
            List<string> ids = new List<string>();
            foreach (PxeFlag f in d.Flags) if (!string.IsNullOrEmpty(f.DeploymentId) && !ids.Contains(f.DeploymentId)) ids.Add(f.DeploymentId);
            foreach (PxeRequest r in d.Requests) if (!string.IsNullOrEmpty(r.OfferId) && !ids.Contains(r.OfferId)) ids.Add(r.OfferId);
            d.DeploymentNames = site.DeploymentNames(ids);
            d.Machines = Merge(d.Flags, d.UnknownRecords, d.Requests, DateTime.UtcNow);
            // A machine that PXE booted as a known device gets that device record, so it shows its name.
            foreach (Machine m in d.Machines)
            {
                m.DeploymentNames = d.DeploymentNames;
                PxeRequest r = m.LastRequest;
                if (m.Devices.Count > 0 || r == null || r.ItemKey <= 0 || r.ItemKey >= Ids.UnknownComputerStart) continue;
                try { m.Devices.AddRange(site.FindDevices(null, null, null, new uint[] { (uint)r.ItemKey })); }
                catch (Exception) { }
            }
            d.ReadUtc = DateTime.UtcNow;
            return d;
        }

        // How long one read waits for SMSPXE.log on the PXE points. A server that is slower is read on in the background,
        // and its lines come with a later read.
        public static TimeSpan LogWait = TimeSpan.FromSeconds(20);

        // Reads SMSPXE.log on every PXE point at the same time, so a slow or unreachable server does not hold up the rest.
        static void ReadLogs(ISite site, PxeLogStore store)
        {
            List<Thread> threads = new List<Thread>();
            foreach (string s in site.Info.PxeServers)
            {
                string server = s;
                if (!store.Begin(server)) continue;
                Thread t = new Thread(delegate ()
                {
                    try
                    {
                        string note;
                        List<LogLine> lines = site.ReadPxeLog(server, out note);
                        store.Add(server, lines);
                        store.Note(server, note);
                    }
                    catch (Exception ex) { store.Note(server, "SMSPXE.log on " + server + " could not be read: " + ex.Message); }
                    finally { store.End(server); }
                });
                t.IsBackground = true;
                t.Start();
                threads.Add(t);
            }
            DateTime until = DateTime.UtcNow + LogWait;
            foreach (Thread t in threads)
            {
                TimeSpan left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || !t.Join(left)) continue;
            }
            foreach (string server in store.Busy()) store.Note(server, "SMSPXE.log on " + server + " is slow to answer. Its lines come with a later read.");
        }

        public static List<Machine> Merge(List<PxeFlag> flags, List<DeviceRecord> devices)
        {
            return Merge(flags, devices, new List<PxeRequest>(), DateTime.UtcNow);
        }

        public static string Key(string mac, string guid)
        {
            if (guid != null) return "guid:" + guid;
            if (mac != null) return "mac:" + mac;
            return null;
        }

        // One machine per SMBIOS GUID, or per MAC address when there is no GUID. A record with neither stays on its own.
        public static List<Machine> Merge(List<PxeFlag> flags, List<DeviceRecord> devices, List<PxeRequest> requests, DateTime now)
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
            // Requests come newest first, so each machine's list stays newest first.
            Dictionary<Machine, bool> fromRequests = new Dictionary<Machine, bool>();
            foreach (PxeRequest r in requests)
            {
                int before = list.Count;
                Machine m = get(r.MacAddress, r.SmbiosGuid);
                if (list.Count > before) fromRequests[m] = true;
                m.Requests.Add(r);
            }
            // A machine known only from its PXE requests is listed while its last boot was recent and not serviced.
            list.RemoveAll(delegate (Machine m)
            {
                if (!fromRequests.ContainsKey(m) || m.Flags.Count > 0 || m.Devices.Count > 0) return false;
                PxeRequest r = m.LastRequest;
                return r == null || !m.NotServiced || !r.Utc.HasValue || now - r.Utc.Value > RequestWindow;
            });
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
                // A MAC address can belong to a network adapter that several devices have used, and some firmware reports the same
                // SMBIOS GUID on many machines. Named devices found only that way are left alone when there is more than one,
                // so a deletion never reaches a device the machine does not clearly own.
                if (guid != null || mac != null)
                {
                    List<DeviceRecord> byKeyOnly = devices.FindAll(delegate (DeviceRecord d) { return !d.IsUnknownRecord && !flagIds.Contains(d.ResourceId); });
                    if (byKeyOnly.Count > 1)
                    {
                        devices.RemoveAll(delegate (DeviceRecord d) { return byKeyOnly.Contains(d); });
                        string key = guid != null ? "SMBIOS GUID " + guid : "MAC address " + mac;
                        p.Notes.Add(m.Name + ": " + byKeyOnly.Count + " devices (" + Names(byKeyOnly) + ") have the " + key + ", probably firmware or a network adapter that many machines share, so they are left alone. Reset a device by its name to clear its flag.");
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

        // The reason a step failed, with what to do when the cause is known. Windows says only "Cannot open WDSServer
        // service on computer" when the rights are missing, and the real reason is in the inner exception.
        // In the text Explain gives when restarting the PXE service is denied. NeedsAdmin looks for it.
        public const string AdminHint = "needs administrator rights on";

        // True for a failed change that PxeVice run as administrator might have done.
        public static bool NeedsAdmin(Activity a)
        {
            return a != null && !a.Ok && a.Result != null && a.Result.IndexOf(AdminHint, StringComparison.Ordinal) >= 0;
        }

        public static string Explain(Exception ex, Step s)
        {
            string text = ex == null ? "" : ex.Message;
            bool denied = false;
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                System.ComponentModel.Win32Exception w = e as System.ComponentModel.Win32Exception;
                if (w != null && w.NativeErrorCode == 5) denied = true;
                if (e is UnauthorizedAccessException || e.Message.IndexOf("access is denied", StringComparison.OrdinalIgnoreCase) >= 0) denied = true;
                if (e != ex && e.Message.Length > 0 && text.IndexOf(e.Message, StringComparison.OrdinalIgnoreCase) < 0) text += " " + e.Message;
            }
            if (denied && s != null && s.Kind == StepKind.RestartService)
                return text.TrimEnd('.', ' ') + ". Restarting the PXE service " + AdminHint + " " + s.Server + ": start PxeVice as administrator when it runs on that server, or use a service account that is a local administrator there.";
            if (denied)
                return text.TrimEnd('.', ' ') + ". The account PxeVice runs with lacks the rights for this change.";
            return text;
        }

        // How long to wait between the reads that check a deleted device is gone. Tests set it to 0.
        public static int VerifyWaitMs = 600;

        static bool Gone(ISite site, uint id)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (attempt > 0 && VerifyWaitMs > 0) System.Threading.Thread.Sleep(VerifyWaitMs);
                try { if (site.FindDevices(null, null, null, new uint[] { id }).Count == 0) return true; }
                catch (Exception) { return true; }
            }
            return false;
        }

        // Runs the steps in order. A step that fails is reported and the next one still runs.
        static int CountMachines(Plan plan)
        {
            List<string> names = new List<string>();
            foreach (Step s in plan.Steps)
                if (s.Kind != StepKind.RestartService && s.MachineName != null && !names.Contains(s.MachineName)) names.Add(s.MachineName);
            return names.Count;
        }

        public static List<Activity> Execute(ISite site, Plan plan, Action<Activity> progress)
        {
            return Execute(site, plan, null, progress);
        }

        // Runs the plan. The policies are checked first, and a device is only deleted after a backup file was written.
        public static List<Activity> Execute(ISite site, Plan plan, ResetContext ctx, Action<Activity> progress)
        {
            List<Activity> done = new List<Activity>();
            string user = Cli.UserName;
            string ticket = ctx == null ? "" : Ticket.Clean(ctx.Ticket);
            IList<Machine> machines = ctx != null ? ctx.Machines : new List<Machine>();
            int count = machines.Count > 0 ? machines.Count : CountMachines(plan);
            string why = PxePolicy.Blocked(plan, count, ticket);
            if (why == null && plan.Count(StepKind.DeleteDevice) > 0)
            {
                try
                {
                    string path = ResetBackup.Write(ctx != null && ctx.BackupFolder != null ? ctx.BackupFolder : ResetBackup.Folder, machines, plan, ticket, user, DateTime.UtcNow);
                    Activity b = new Activity();
                    b.TimeUtc = DateTime.UtcNow;
                    b.Machine = machines.Count > 0 ? machines[0].Name : "Reset";
                    b.Action = "Wrote what is known before the deletion";
                    b.Result = path;
                    b.User = user;
                    b.Ticket = ticket;
                    b.Ok = true;
                    done.Add(b);
                    ActivityLog.Write(b);
                    if (progress != null) progress(b);
                }
                catch (Exception ex)
                {
                    why = "The backup file could not be written (" + ex.Message + "), so no device is deleted and nothing was changed.";
                }
            }
            if (why != null)
            {
                Activity blocked = new Activity();
                blocked.TimeUtc = DateTime.UtcNow;
                blocked.Machine = machines.Count > 0 ? machines[0].Name : "Reset";
                blocked.Action = "Reset not started";
                blocked.Result = why;
                blocked.User = user;
                blocked.Ticket = ticket;
                blocked.Ok = false;
                done.Add(blocked);
                ActivityLog.Write(blocked);
                if (progress != null) progress(blocked);
                return done;
            }
            foreach (Step s in plan.Steps)
            {
                Activity a = new Activity();
                a.TimeUtc = DateTime.UtcNow;
                a.Machine = s.MachineName ?? s.Server;
                a.Action = s.Text;
                a.User = user;
                a.Ticket = ticket;
                try
                {
                    switch (s.Kind)
                    {
                        case StepKind.ClearFlag:
                            try
                            {
                                site.ClearFlags(s.ResourceIds);
                                a.Result = "Cleared";
                            }
                            catch (System.Management.ManagementException mex)
                            {
                                // A device the site has already let go of has no flag to clear. The rows that are left are deleted by the next step.
                                if (mex.ErrorCode != System.Management.ManagementStatus.NotFound) throw;
                                a.Result = "The site no longer has this device, so there is nothing to clear";
                            }
                            break;
                        case StepKind.DeleteUnknownRecord:
                            site.DeleteDevice(s.ResourceIds[0]);
                            a.Result = "Deleted";
                            break;
                        case StepKind.DeleteDevice:
                            site.DeleteDevice(s.ResourceIds[0]);
                            // Deleting only marks the device Decommissioned. Read it again to see that the site let go of it.
                            if (Gone(site, s.ResourceIds[0])) a.Result = "Deleted (decommissioned)";
                            else
                            {
                                a.Result = "Deleted, but the site still lists the device. Read again in a moment";
                                a.Ok = false;
                                done.Add(a);
                                ActivityLog.Write(a);
                                if (progress != null) progress(a);
                                continue;
                            }
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
                    a.Result = Explain(ex, s);
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
            string text = (a.Machine ?? "") + ": " + a.Action + ". " + (a.Ok ? "" : "Failed: ") + a.Result + " (by " + a.User + (string.IsNullOrEmpty(a.Ticket) ? "" : ", ticket " + a.Ticket) + ")";
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
