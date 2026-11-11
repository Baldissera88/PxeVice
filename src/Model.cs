namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Text.RegularExpressions;

    // A row in dbo.LastPXEAdvertisement: the record that a PXE deployment was offered to a machine.
    public sealed class PxeFlag
    {
        public long MachineId;
        public string MacAddress;
        public string RawMac;
        public string SmbiosGuid;
        public string Netbios;
        public string DeploymentId;
        public DateTime? TimeUtc;
        public string SiteCode;
        // Name0 of the device in v_R_System that MachineId points to, when it still exists.
        public string DeviceName;

        public bool IsUnknownComputer
        {
            get { return MachineId == 0 || MachineId >= Ids.UnknownComputerStart; }
        }

        public bool IsDeletedDevice
        {
            get { return !IsUnknownComputer && DeviceName == null; }
        }

        public string Owner
        {
            get
            {
                if (IsUnknownComputer) return "Unknown computer";
                if (DeviceName != null) return DeviceName;
                return string.IsNullOrEmpty(Netbios) ? "Deleted device" : Netbios + " (deleted)";
            }
        }
    }

    // A device in SMS_R_System.
    public sealed class DeviceRecord
    {
        public uint ResourceId;
        public string Name;
        public List<string> MacAddresses = new List<string>();
        public string SmbiosGuid;
        public DateTime? CreatedUtc;
        public bool Client;

        public bool IsUnknownRecord
        {
            get { return string.Equals(Name, "Unknown", StringComparison.OrdinalIgnoreCase); }
        }
    }

    public static class MachineKinds
    {
        public const string Unknown = "Unknown computer";
        public const string UnknownRecord = "Unknown device record";
        public const string Deleted = "Deleted device";
        public const string Device = "Device";
    }

    // One machine: every PXE flag and device record that share its SMBIOS GUID, or its MAC address when there is no GUID.
    public sealed class Machine
    {
        public string Key;
        public string MacAddress;
        public string SmbiosGuid;
        public readonly List<PxeFlag> Flags = new List<PxeFlag>();
        public readonly List<DeviceRecord> Devices = new List<DeviceRecord>();
        public Dictionary<string, string> DeploymentNames;

        public string Name
        {
            get
            {
                foreach (DeviceRecord d in Devices) if (!d.IsUnknownRecord) return d.Name;
                foreach (PxeFlag f in Flags) if (f.DeviceName != null) return f.DeviceName;
                foreach (PxeFlag f in Flags) if (!string.IsNullOrEmpty(f.Netbios)) return f.Netbios;
                if (Devices.Count > 0) return Devices[0].Name;
                return HasDeletedFlag ? "Deleted device" : "Unknown computer";
            }
        }

        public bool HasUnknownRecord
        {
            get { foreach (DeviceRecord d in Devices) if (d.IsUnknownRecord) return true; return false; }
        }

        public bool HasNamedDevice
        {
            get { foreach (DeviceRecord d in Devices) if (!d.IsUnknownRecord) return true; return false; }
        }

        // A flag on a device that still exists in the console.
        public bool HasDeviceFlag
        {
            get { foreach (PxeFlag f in Flags) if (!f.IsUnknownComputer && f.DeviceName != null) return true; return false; }
        }

        public bool HasUnknownFlag
        {
            get { foreach (PxeFlag f in Flags) if (f.IsUnknownComputer) return true; return false; }
        }

        public bool HasDeletedFlag
        {
            get { foreach (PxeFlag f in Flags) if (f.IsDeletedDevice) return true; return false; }
        }

        // What is left behind, in the words a supporter would use.
        public string Found
        {
            get
            {
                List<string> parts = new List<string>();
                if (Flags.Count > 0) parts.Add(Flags.Count == 1 ? "PXE flag" : Flags.Count + " PXE flags");
                if (HasUnknownRecord) parts.Add("Unknown record");
                if (HasNamedDevice) parts.Add("device");
                return parts.Count == 0 ? "Nothing" : Txt.Sentence(string.Join(" + ", parts.ToArray()));
            }
        }

        public string Kind
        {
            get
            {
                if (HasNamedDevice || HasDeviceFlag) return MachineKinds.Device;
                if (HasUnknownRecord) return MachineKinds.UnknownRecord;
                if (HasDeletedFlag) return MachineKinds.Deleted;
                return MachineKinds.Unknown;
            }
        }

        public DateTime? LastPxeUtc
        {
            get
            {
                DateTime? last = null;
                foreach (PxeFlag f in Flags) if (f.TimeUtc.HasValue && (!last.HasValue || f.TimeUtc.Value > last.Value)) last = f.TimeUtc;
                return last;
            }
        }

        // The newest time anything was recorded for this machine: its last PXE flag, or when an Unknown record was created.
        public DateTime? LastSeenUtc
        {
            get
            {
                DateTime? last = LastPxeUtc;
                foreach (DeviceRecord d in Devices) if (d.CreatedUtc.HasValue && (!last.HasValue || d.CreatedUtc.Value > last.Value)) last = d.CreatedUtc;
                return last;
            }
        }

        public string Deployments
        {
            get
            {
                List<string> ids = new List<string>();
                foreach (PxeFlag f in Flags) if (!string.IsNullOrEmpty(f.DeploymentId) && !ids.Contains(f.DeploymentId)) ids.Add(f.DeploymentId);
                return string.Join(", ", ids.ToArray());
            }
        }

        public string DeploymentText(string id)
        {
            string name;
            if (DeploymentNames != null && id != null && DeploymentNames.TryGetValue(id, out name) && !string.IsNullOrEmpty(name)) return id + " (" + name + ")";
            return id;
        }

        public string ResourceIds
        {
            get
            {
                List<string> ids = new List<string>();
                foreach (DeviceRecord d in Devices) ids.Add(d.ResourceId.ToString(CultureInfo.InvariantCulture));
                return string.Join(", ", ids.ToArray());
            }
        }

        public List<string> AllMacs
        {
            get
            {
                List<string> list = new List<string>();
                if (MacAddress != null) list.Add(MacAddress);
                foreach (PxeFlag f in Flags) if (f.MacAddress != null && !list.Contains(f.MacAddress)) list.Add(f.MacAddress);
                foreach (DeviceRecord d in Devices) foreach (string m in d.MacAddresses) if (!list.Contains(m)) list.Add(m);
                return list;
            }
        }
    }

    public enum StepKind
    {
        ClearFlag,
        DeleteUnknownRecord,
        DeleteDevice,
        DeleteFlagRows,
        RestartService
    }

    // One change a reset makes.
    public sealed class Step
    {
        public StepKind Kind;
        public string MachineName;
        public string Target;
        public uint[] ResourceIds = new uint[0];
        public string MacAddress;
        public string SmbiosGuid;
        public int Rows;
        public string Server;
        public string Service;

        public string Text
        {
            get
            {
                switch (Kind)
                {
                    case StepKind.ClearFlag: return "Clear the PXE flag on " + Target + " with Clear Required PXE Deployments";
                    case StepKind.DeleteUnknownRecord: return "Delete the leftover device record " + Target;
                    case StepKind.DeleteDevice: return "Delete the device record " + Target;
                    case StepKind.DeleteFlagRows: return "Delete " + Rows + (Rows == 1 ? " row" : " rows") + " for " + Target + " from LastPXEAdvertisement";
                    case StepKind.RestartService: return "Restart " + Service + " on " + Server;
                }
                return Kind.ToString();
            }
        }
    }

    public sealed class Plan
    {
        public readonly List<Step> Steps = new List<Step>();
        public readonly List<string> Notes = new List<string>();

        public int Count(StepKind k)
        {
            int n = 0;
            foreach (Step s in Steps) if (s.Kind == k) n++;
            return n;
        }
    }

    // A line on the Activity tab and in the activity log.
    public sealed class Activity
    {
        public DateTime TimeUtc;
        public string Machine;
        public string Action;
        public bool Ok;
        public string Result;
        public string User;
    }

    public static class Ids
    {
        // Resource IDs from this value and up belong to the x86, x64 and ARM64 Unknown Computer objects.
        public const long UnknownComputerStart = 2046820352;

        static readonly Regex MacPattern = new Regex("[0-9A-Fa-f]{2}([:-]?[0-9A-Fa-f]{2}){5}", RegexOptions.CultureInvariant);

        // The first MAC address in the text, as 00:15:5D:23:21:3B. Null when there is none.
        public static string Mac(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            Match m = MacPattern.Match(value);
            if (!m.Success) return null;
            string hex = m.Value.Replace(":", "").Replace("-", "").ToUpperInvariant();
            StringBuilder b = new StringBuilder(17);
            for (int i = 0; i < 12; i += 2)
            {
                if (i > 0) b.Append(':');
                b.Append(hex, i, 2);
            }
            return b.ToString();
        }

        // Every MAC address in the text.
        public static List<string> Macs(string value)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(value)) return list;
            foreach (Match m in MacPattern.Matches(value))
            {
                string mac = Mac(m.Value);
                if (mac != null && !list.Contains(mac)) list.Add(mac);
            }
            return list;
        }

        // An SMBIOS GUID in upper case with dashes. Null when the text is not a GUID or is all zeros.
        public static string Guid(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            System.Guid g;
            try { g = new System.Guid(value.Trim()); }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }
            if (g == System.Guid.Empty) return null;
            return g.ToString("D").ToUpperInvariant();
        }
    }

    public static class Txt
    {
        public static string Short(string s, int max)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, Math.Max(0, max - 1)) + "…";
        }

        public static string Sentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        public static string Local(DateTime? utc)
        {
            if (!utc.HasValue) return "";
            return utc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        // "5 min ago", "3 h ago", "2 days ago".
        public static string Ago(DateTime? utc, DateTime nowUtc)
        {
            if (!utc.HasValue) return "";
            TimeSpan d = nowUtc - utc.Value;
            if (d.TotalMinutes < 1) return "just now";
            if (d.TotalMinutes < 60) return ((int)d.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " min ago";
            if (d.TotalHours < 24) return ((int)d.TotalHours).ToString(CultureInfo.InvariantCulture) + " h ago";
            int days = (int)d.TotalDays;
            return days.ToString(CultureInfo.InvariantCulture) + (days == 1 ? " day ago" : " days ago");
        }
    }
}
