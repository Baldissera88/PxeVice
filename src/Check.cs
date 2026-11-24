namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    // A task sequence deployment, as the PXE point sees it.
    public sealed class TsDeployment
    {
        // AdvertFlags of SMS_Advertisement.
        public const uint FlagMediaAndPxe = 0x00040000;
        public const uint FlagOnlyMediaAndPxe = 0x10000000;
        public const uint FlagHiddenInWinPe = 0x20000000;

        public string Id;
        public string Name;
        public string PackageId;
        public string BootImageId;
        public string CollectionId;
        public string CollectionName;
        public uint Flags;
        public bool Required;
        public DateTime? AvailableUtc;
        public DateTime? ExpiresUtc;

        // Made available to "Configuration Manager clients, media and PXE" or "Only media and PXE".
        public bool ForPxe
        {
            get { return (Flags & FlagHiddenInWinPe) == 0 && ((Flags & FlagMediaAndPxe) != 0 || (Flags & FlagOnlyMediaAndPxe) != 0); }
        }

        public string AvailableTo
        {
            get
            {
                if ((Flags & FlagHiddenInWinPe) != 0) return "Only Configuration Manager clients";
                if ((Flags & FlagOnlyMediaAndPxe) != 0) return "Only media and PXE";
                if ((Flags & FlagMediaAndPxe) != 0) return "Configuration Manager clients, media and PXE";
                return "Only Configuration Manager clients";
            }
        }

        public bool NotYet(DateTime now)
        {
            return AvailableUtc.HasValue && AvailableUtc.Value > now;
        }

        public bool Expired(DateTime now)
        {
            return ExpiresUtc.HasValue && ExpiresUtc.Value <= now;
        }

        public string Text
        {
            get { return Id + " " + (Name ?? PackageId) + (Required ? " (required)" : " (available)"); }
        }
    }

    // A distribution point that answers PXE.
    public sealed class PxePoint
    {
        public string Server;
        // Null when the site does not say.
        public bool? SupportsUnknown;
        public bool? Active;
    }

    public enum Severity
    {
        Blocks,
        Warning,
        Info,
        Ok
    }

    public sealed class Finding
    {
        public Severity Severity;
        public string Title;
        public string What;
        public string ToDo;
        public readonly List<string> Evidence = new List<string>();

        public string SeverityText
        {
            get
            {
                switch (Severity)
                {
                    case Severity.Blocks: return "Blocks PXE";
                    case Severity.Warning: return "Check";
                    case Severity.Ok: return "OK";
                }
                return "Note";
            }
        }

        public static Finding Make(Severity s, string title, string what, string todo)
        {
            Finding f = new Finding();
            f.Severity = s;
            f.Title = title;
            f.What = what;
            f.ToDo = todo;
            return f;
        }
    }

    // What the check reads from the site once per refresh, and per resource as it is needed.
    public sealed class CheckCache
    {
        public List<TsDeployment> Deployments;
        public string DeploymentsError;
        public List<PxePoint> Points;
        public string PointsError;
        public readonly Dictionary<uint, List<string>> Collections = new Dictionary<uint, List<string>>();
        public readonly Dictionary<string, List<string>> Content = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<Finding>> Results = new Dictionary<string, List<Finding>>(StringComparer.OrdinalIgnoreCase);
        readonly object gate = new object();

        public void Load(ISite site)
        {
            lock (gate)
            {
                if (Deployments == null && DeploymentsError == null)
                {
                    try { Deployments = site.TaskSequenceDeployments(); }
                    catch (Exception ex) { DeploymentsError = ex.Message; }
                }
                if (Points == null && PointsError == null)
                {
                    try { Points = site.PxePoints(); }
                    catch (Exception ex) { PointsError = ex.Message; }
                }
            }
        }

        public List<string> CollectionsOf(ISite site, uint id)
        {
            lock (gate)
            {
                List<string> l;
                if (!Collections.TryGetValue(id, out l))
                {
                    l = site.CollectionsOf(id);
                    Collections[id] = l;
                }
                return l;
            }
        }

        public List<string> ContentOn(ISite site, string packageId)
        {
            lock (gate)
            {
                List<string> l;
                if (!Content.TryGetValue(packageId, out l))
                {
                    l = site.ContentServers(packageId);
                    Content[packageId] = l;
                }
                return l;
            }
        }

        public TsDeployment Deployment(string id)
        {
            if (Deployments == null || id == null) return null;
            foreach (TsDeployment d in Deployments) if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }
    }

    public static class Checker
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static readonly uint[] UnknownComputerIds = { 2046820352, 2046820353, 2046820354 };

        public static bool SameServer(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            string x = a.Split('.')[0], y = b.Split('.')[0];
            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        // Everything that decides whether the machine gets a task sequence when it PXE boots, worst first.
        public static List<Finding> Check(ISite site, Machine m, CheckCache cache, DateTime now)
        {
            List<Finding> list = new List<Finding>();
            cache.Load(site);
            PxeRequest last = m.LastRequest;

            // Device records of the machine as the site has them now.
            List<DeviceRecord> devices = new List<DeviceRecord>(m.Devices);
            try
            {
                List<uint> known = new List<uint>();
                foreach (DeviceRecord d in devices) known.Add(d.ResourceId);
                if (last != null && last.ItemKey > 0 && last.ItemKey < Ids.UnknownComputerStart) known.Add((uint)last.ItemKey);
                if (m.MacAddress != null || m.SmbiosGuid != null || known.Count > 0)
                {
                    foreach (DeviceRecord d in site.FindDevices(m.MacAddress, m.SmbiosGuid, null, known.ToArray()))
                    {
                        bool have = false;
                        foreach (DeviceRecord x in devices) if (x.ResourceId == d.ResourceId) have = true;
                        if (!have) devices.Add(d);
                    }
                }
            }
            catch (Exception ex) { list.Add(Finding.Make(Severity.Info, "Device records could not be read", ex.Message, null)); }

            Flags(list, m, cache);
            Records(list, m, devices, last);
            LastBoot(list, m, now);
            Deployments(list, site, m, devices, last, cache, now);

            list.Sort(delegate (Finding a, Finding b) { return ((int)a.Severity).CompareTo((int)b.Severity); });
            return list;
        }

        static void Flags(List<Finding> list, Machine m, CheckCache cache)
        {
            foreach (PxeFlag f in m.Flags)
            {
                TsDeployment d = cache.Deployment(f.DeploymentId);
                string when = f.TimeUtc.HasValue ? " on " + Txt.Local(f.TimeUtc) : "";
                string owner = f.IsUnknownComputer ? "the Unknown Computer object, so the console cannot clear it for this machine alone" : f.IsDeletedDevice ? "a device that was deleted, so the console cannot show it" : "the device " + f.Owner;
                Finding x;
                if (d != null && !d.Required)
                {
                    x = Finding.Make(Severity.Info, "PXE flag for an available deployment",
                        "Deployment " + m.DeploymentText(f.DeploymentId) + " was offered" + when + ". It is available, so the flag does not stop it from being offered again. The flag is recorded for " + owner + ".",
                        "Nothing needed for this deployment. A reset removes the flag anyway.");
                }
                else
                {
                    x = Finding.Make(f.IsDeletedDevice ? Severity.Warning : Severity.Blocks, d != null ? "Required deployment already offered" : "PXE flag",
                        "Deployment " + m.DeploymentText(f.DeploymentId) + " was offered through PXE" + when + ". " + (d != null ? "It is required, and a required deployment is not offered again while this flag is there." : "A required deployment is not offered again while this flag is there.") + " The flag is recorded for " + owner + ".",
                        "Reset the machine to clear the flag.");
                }
                x.Evidence.Add("LastPXEAdvertisement: MachineID " + f.MachineId.ToString(Inv) + ", " + (f.RawMac ?? "") + ", " + (f.SmbiosGuid ?? "no SMBIOS GUID") + ", " + f.DeploymentId + ", " + Txt.Local(f.TimeUtc));
                list.Add(x);
            }
        }

        static void Records(List<Finding> list, Machine m, List<DeviceRecord> devices, PxeRequest last)
        {
            List<DeviceRecord> unknown = devices.FindAll(delegate (DeviceRecord d) { return d.IsUnknownRecord; });
            List<DeviceRecord> named = devices.FindAll(delegate (DeviceRecord d) { return !d.IsUnknownRecord; });
            foreach (DeviceRecord d in unknown)
            {
                Finding x = Finding.Make(Severity.Blocks, "Leftover Unknown device record",
                    "Device " + d.ResourceId.ToString(Inv) + " is named Unknown and has this machine's MAC address or SMBIOS GUID. A task sequence created it on an unknown computer and failed before the computer got a name. While it exists the machine is known, so deployments to Unknown Computers do not apply to it.",
                    "Reset the machine. PxeVice deletes Unknown records unless you turn that off.");
                x.Evidence.Add("SMS_R_System " + d.ResourceId.ToString(Inv) + ": Unknown, " + string.Join(", ", d.MacAddresses.ToArray()) + ", " + (d.SmbiosGuid ?? "no SMBIOS GUID") + (d.CreatedUtc.HasValue ? ", created " + Txt.Local(d.CreatedUtc) : ""));
                list.Add(x);
            }
            if (devices.Count > 1)
            {
                List<string> names = new List<string>();
                foreach (DeviceRecord d in devices) names.Add(d.Name + " (" + d.ResourceId.ToString(Inv) + ")");
                Finding x = Finding.Make(Severity.Warning, "Several device records",
                    devices.Count + " device records share this machine's MAC address or SMBIOS GUID: " + string.Join(", ", names.ToArray()) + ". The PXE point answers for one of them, which may not be the one the deployment is for.",
                    named.Count > 1 ? "Delete the records of the old computer, so one record is left." : "Reset the machine to remove the Unknown record.");
                list.Add(x);
            }
            if (last != null && (last.DuplicateSmbios || last.DuplicateMac))
            {
                string what = last.DuplicateSmbios && last.DuplicateMac ? "SMBIOS GUID and MAC address" : last.DuplicateSmbios ? "SMBIOS GUID" : "MAC address";
                Finding x = Finding.Make(Severity.Warning, "Duplicate " + what,
                    "The site told the PXE point that more than one record has this " + what + ". That happens with a firmware that reports the same SMBIOS GUID on many machines, or a USB network adapter used on several. The PXE point may answer for the wrong record.",
                    last.DuplicateSmbios ? "Add the SMBIOS GUID under Hierarchy Settings > Client Approval and Conflicting Records > Duplicate hardware identifiers, and delete the old records." : "Add the MAC address under Hierarchy Settings > Client Approval and Conflicting Records > Duplicate hardware identifiers.");
                foreach (LogLine l in last.Lines) if (l.Text.IndexOf("<ClientIDReply", StringComparison.OrdinalIgnoreCase) >= 0) x.Evidence.Add(Txt.Short(l.Text, 400));
                list.Add(x);
            }
        }

        static void LastBoot(List<Finding> list, Machine m, DateTime now)
        {
            PxeRequest r = m.LastRequest;
            if (r == null) return;
            string when = Txt.Local(r.Utc) + " (" + Txt.Ago(r.Utc, now) + ") on " + r.Server;
            Finding x;
            if (r.Served)
            {
                x = Finding.Make(Severity.Ok, "Last PXE boot was offered a task sequence",
                    "At " + when + " the PXE point offered " + m.DeploymentText(r.OfferId) + (r.Mandatory == true ? " as required" : r.Mandatory == false ? " as available, so it needed F12 or a choice in the wizard" : "") + ".", null);
            }
            else if (r.Outcome == RequestOutcome.Open)
            {
                x = Finding.Make(Severity.Info, "Last PXE boot", "At " + when + " the machine sent a PXE request. SMSPXE.log has no answer for it yet.", null);
            }
            else
            {
                x = Finding.Make(Severity.Blocks, "Last PXE boot was not serviced",
                    "At " + when + " the PXE point found no deployment for the machine" + (r.ItemKey >= 0 ? ", answering for " + (r.IsUnknownComputer ? "the Unknown Computer object " : "the device ") + r.ItemKey.ToString(Inv) : "") + ", so it booted from its disk. The other findings say why.",
                    null);
            }
            foreach (LogLine l in r.Lines) x.Evidence.Add(Txt.Local(l.Utc) + "  " + Txt.Short(l.Text, 2000));
            list.Add(x);
        }

        static void Deployments(List<Finding> list, ISite site, Machine m, List<DeviceRecord> devices, PxeRequest last, CheckCache cache, DateTime now)
        {
            if (cache.Deployments == null)
            {
                list.Add(Finding.Make(Severity.Info, "Deployments could not be checked", cache.DeploymentsError ?? "The task sequence deployments were not read.", null));
                return;
            }
            // The resources the PXE point answers for: the device records, or the Unknown Computer objects.
            bool unknown = devices.Count == 0;
            List<uint> targets = new List<uint>();
            if (!unknown) foreach (DeviceRecord d in devices) targets.Add(d.ResourceId);
            else if (last != null && last.ItemKey >= Ids.UnknownComputerStart && last.ItemKey <= uint.MaxValue) targets.Add((uint)last.ItemKey);
            else targets.AddRange(UnknownComputerIds);

            List<string> collections = new List<string>();
            try
            {
                foreach (uint id in targets) foreach (string c in cache.CollectionsOf(site, id)) if (!collections.Contains(c)) collections.Add(c);
            }
            catch (Exception ex)
            {
                list.Add(Finding.Make(Severity.Info, "Collections could not be read", ex.Message, null));
                return;
            }
            List<TsDeployment> reach = cache.Deployments.FindAll(delegate (TsDeployment d) { return collections.Contains(d.CollectionId); });
            string who = unknown ? "the Unknown Computer objects" : devices.Count == 1 ? "the device " + devices[0].Name + " (" + devices[0].ResourceId.ToString(Inv) + ")" : "the device records";
            if (reach.Count == 0)
            {
                Finding x = Finding.Make(Severity.Blocks, "No task sequence is deployed to it",
                    "No task sequence deployment targets a collection " + who + (unknown ? " are" : " is") + " in" + (collections.Count > 0 ? " (" + collections.Count + (collections.Count == 1 ? " collection" : " collections") + ")" : "") + ".",
                    unknown ? "Deploy a task sequence to All Unknown Computers, or a collection with them, and make it available to Configuration Manager clients, media and PXE." :
                        devices.TrueForAll(delegate (DeviceRecord d) { return d.IsUnknownRecord; }) ? "Reset the machine so the Unknown record is deleted and it boots as an unknown computer." :
                        "Add the device to a collection with a task sequence deployment. Or, as a last resort and high risk, delete the device from the site so it boots as an unknown computer: it loses its collection memberships. Make sure a task sequence is deployed to All Unknown Computers first.");
                foreach (string c in collections) x.Evidence.Add("Member of " + c);
                list.Add(x);
                return;
            }

            List<TsDeployment> pxe = reach.FindAll(delegate (TsDeployment d) { return d.ForPxe && !d.NotYet(now) && !d.Expired(now); });
            foreach (TsDeployment d in reach)
            {
                if (pxe.Contains(d)) continue;
                Finding x;
                if (!d.ForPxe)
                    x = Finding.Make(pxe.Count == 0 ? Severity.Blocks : Severity.Info, "Deployment not available to PXE",
                        d.Text + " to " + Coll(d) + " is made available to: " + d.AvailableTo + ". PXE only offers deployments that are available to media and PXE.",
                        "In the deployment's properties, on Deployment Settings, set Make available to the following to Configuration Manager clients, media and PXE, or Only media and PXE.");
                else if (d.NotYet(now))
                    x = Finding.Make(pxe.Count == 0 ? Severity.Blocks : Severity.Info, "Deployment not available yet",
                        d.Text + " to " + Coll(d) + " is available from " + Txt.Local(d.AvailableUtc) + ".", "Wait, or change the available time in the deployment's Scheduling.");
                else
                    x = Finding.Make(pxe.Count == 0 ? Severity.Blocks : Severity.Info, "Deployment expired",
                        d.Text + " to " + Coll(d) + " expired " + Txt.Local(d.ExpiresUtc) + ".", "Change or remove the expiry time in the deployment's Scheduling.");
                list.Add(x);
            }

            List<string> servers = new List<string>();
            if (last != null && !string.IsNullOrEmpty(last.Server)) servers.Add(last.Server);
            else if (cache.Points != null) foreach (PxePoint p in cache.Points) servers.Add(p.Server);
            else servers.AddRange(site.Info.PxeServers);

            foreach (TsDeployment d in pxe)
            {
                bool flagged = false;
                foreach (PxeFlag f in m.Flags) if (string.Equals(f.DeploymentId, d.Id, StringComparison.OrdinalIgnoreCase)) flagged = true;
                if (string.IsNullOrEmpty(d.BootImageId))
                {
                    list.Add(Finding.Make(Severity.Warning, "Task sequence without a boot image", d.Text + " has no boot image, so PXE cannot start it.", "Set a boot image in the task sequence's properties, on Advanced."));
                    continue;
                }
                if (servers.Count > 0)
                {
                    List<string> on;
                    try { on = cache.ContentOn(site, d.BootImageId); }
                    catch (Exception ex)
                    {
                        list.Add(Finding.Make(Severity.Info, "Boot image could not be checked", d.BootImageId + ": " + ex.Message, null));
                        on = null;
                    }
                    if (on != null)
                    {
                        List<string> missing = servers.FindAll(delegate (string s) { return !on.Exists(delegate (string o) { return SameServer(o, s); }); });
                        if (missing.Count == servers.Count)
                        {
                            Finding x = Finding.Make(Severity.Blocks, "Boot image not on the PXE distribution point",
                                "The boot image " + d.BootImageId + " of " + d.Text + " is not on " + string.Join(", ", missing.ToArray()) + ". The PXE point only offers task sequences whose boot image it has.",
                                "Distribute the boot image to " + string.Join(", ", missing.ToArray()) + ", and check that Deploy this boot image from the PXE-enabled distribution point is on in its Data Source tab.");
                            if (on.Count > 0) x.Evidence.Add(d.BootImageId + " is on " + string.Join(", ", on.ToArray()));
                            list.Add(x);
                            continue;
                        }
                    }
                }
                if (d.Required && flagged) continue;
                list.Add(Finding.Make(Severity.Ok, "Can get " + d.Id,
                    d.Text + " to " + Coll(d) + ", available to " + (d.AvailableTo.StartsWith("Only") ? "only" + d.AvailableTo.Substring(4) : d.AvailableTo) + ", boot image " + d.BootImageId + "." + (d.Required ? "" : " It is available, so the user presses F12 when asked and chooses it."), null));
            }

            if (unknown && cache.Points != null)
            {
                foreach (PxePoint p in cache.Points)
                {
                    if (servers.Count > 0 && !servers.Exists(delegate (string s) { return SameServer(s, p.Server); })) continue;
                    if (p.SupportsUnknown == false)
                        list.Add(Finding.Make(Severity.Blocks, "Unknown computer support is off",
                            "The machine boots as an unknown computer, and " + p.Server + " does not answer unknown computers.",
                            "On the PXE tab of the distribution point, turn on Enable unknown computer support, or import the computer into Configuration Manager."));
                }
            }
        }

        static string Coll(TsDeployment d)
        {
            return string.IsNullOrEmpty(d.CollectionName) ? d.CollectionId : d.CollectionName + " (" + d.CollectionId + ")";
        }

        public static bool Blocked(List<Finding> f)
        {
            return f != null && f.Exists(delegate (Finding x) { return x.Severity == Severity.Blocks; });
        }

        // The findings as plain text, for a ticket or the console.
        public static string AsText(Machine m, List<Finding> findings, DateTime now)
        {
            StringBuilder b = new StringBuilder();
            b.Append(m.Name).Append("\r\n");
            if (m.MacAddress != null) b.Append("MAC address: ").Append(string.Join(", ", m.AllMacs.ToArray())).Append("\r\n");
            if (m.SmbiosGuid != null) b.Append("SMBIOS GUID: ").Append(m.SmbiosGuid).Append("\r\n");
            if (m.LastRequest != null) b.Append("Last PXE boot: ").Append(Txt.Local(m.LastRequest.Utc)).Append(" on ").Append(m.LastRequest.Server).Append(", ").Append(m.LastRequest.Result).Append("\r\n");
            b.Append("\r\n");
            b.Append(Summary.AsText(Summary.From(m, findings, now))).Append("\r\n");
            foreach (Finding f in findings)
            {
                b.Append("[").Append(f.SeverityText).Append("] ").Append(f.Title).Append("\r\n");
                if (!string.IsNullOrEmpty(f.What)) b.Append("  ").Append(f.What).Append("\r\n");
                if (!string.IsNullOrEmpty(f.ToDo)) b.Append("  To do: ").Append(f.ToDo).Append("\r\n");
                foreach (string e in f.Evidence) b.Append("    ").Append(e).Append("\r\n");
                b.Append("\r\n");
            }
            return b.ToString().TrimEnd() + "\r\n";
        }
    }
}
