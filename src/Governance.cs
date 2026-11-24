namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using Microsoft.Win32;

    // Rules an administrator can set for everyone, as values under HKLM\SOFTWARE\Policies\PxeVice.
    public static class PxePolicy
    {
        public const string ReadOnlyName = "ReadOnly";
        public const string MaxMachinesName = "MaxMachinesPerReset";
        public const string RequireTicketName = "RequireTicket";
        public const string NoDeleteName = "DisableDeviceDeletion";

        // Reads a policy value of this PC, 0 when it is not set. Replaced in the tests.
        public static Func<string, long> Read = ReadValue;

        static long ReadValue(string name)
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = b.OpenSubKey(Updater.PolicyKey))
                {
                    if (k == null) return 0;
                    long n;
                    return long.TryParse(Convert.ToString(k.GetValue(name), CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
                }
            }
            catch (Exception) { return 0; }
        }

        public static bool ReadOnly { get { return Read(ReadOnlyName) != 0; } }
        public static bool RequireTicket { get { return Read(RequireTicketName) != 0; } }
        public static bool NoDeletion { get { return Read(NoDeleteName) != 0; } }
        public static int MaxMachines { get { long n = Read(MaxMachinesName); return n > 0 && n < int.MaxValue ? (int)n : 0; } }

        // Null when the reset may go on, otherwise why it may not.
        public static string Blocked(Plan plan, int machines, string ticket)
        {
            if (ReadOnly) return "Your organization has set PxeVice to read only, with the policy value " + ReadOnlyName + ". Nothing can be changed.";
            if (plan != null && NoDeletion && plan.Count(StepKind.DeleteDevice) > 0)
                return "Your organization does not allow PxeVice to delete devices, with the policy value " + NoDeleteName + ". Turn off the deletion of the device and reset again.";
            int max = MaxMachines;
            if (max > 0 && machines > max)
                return "Your organization allows " + max + (max == 1 ? " machine" : " machines") + " in one reset, with the policy value " + MaxMachinesName + ". You chose " + machines + ".";
            if (RequireTicket && string.IsNullOrEmpty(Ticket.Clean(ticket)))
                return "Your organization asks for a ticket or reference with every reset, with the policy value " + RequireTicketName + ".";
            return null;
        }
    }

    // The ticket or reference a supporter writes with a reset, kept in the activity log.
    public static class Ticket
    {
        public const int MaxLength = 60;

        // One line without control characters and brackets, at most 60 characters. Empty when there is nothing.
        public static string Clean(string value)
        {
            if (value == null) return "";
            StringBuilder b = new StringBuilder();
            foreach (char c in value)
            {
                if (char.IsControl(c) || c == '[' || c == ']' || c == '<' || c == '>') b.Append(' ');
                else b.Append(c);
            }
            string s = Regex.Replace(b.ToString(), "\\s+", " ").Trim();
            return s.Length <= MaxLength ? s : s.Substring(0, MaxLength).TrimEnd();
        }
    }

    // What a reset is for, and who and what it covers.
    public sealed class ResetContext
    {
        public string Ticket = "";
        public IList<Machine> Machines = new List<Machine>();
        // Where backups go. Replaced in the tests.
        public string BackupFolder;
    }

    // Before a device is deleted, what PxeVice knows about it is written to a file, so someone can see what was lost.
    // The file is a record to read, not something PxeVice restores from.
    public static class ResetBackup
    {
        // Replaced in the tests.
        public static string FolderOverride;

        public static string Folder
        {
            get { return FolderOverride ?? Path.Combine(ActivityLog.Folder, "resets"); }
        }

        public static string Text(IList<Machine> machines, Plan plan, string ticket, string user, DateTime nowUtc)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("PxeVice " + Cli.Version + ": what was known before a reset");
            b.AppendLine("Time:    " + nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            b.AppendLine("By:      " + user);
            if (!string.IsNullOrEmpty(Ticket.Clean(ticket))) b.AppendLine("Ticket:  " + Ticket.Clean(ticket));
            b.AppendLine();
            b.AppendLine("Changes:");
            foreach (Step s in plan.Steps) b.AppendLine("  " + (s.HighRisk ? "HIGH RISK  " : "") + s.Text);
            foreach (Machine m in machines)
            {
                b.AppendLine();
                b.AppendLine("Machine: " + m.Name);
                if (m.MacAddress != null) b.AppendLine("  MAC address: " + m.MacAddress);
                if (m.SmbiosGuid != null) b.AppendLine("  SMBIOS GUID: " + m.SmbiosGuid);
                foreach (DeviceRecord d in m.Devices)
                    b.AppendLine("  Device record: " + d.Name + ", resource ID " + d.ResourceId + ", client " + (d.Client ? "yes" : "no") +
                        (d.CreatedUtc.HasValue ? ", created " + Txt.Local(d.CreatedUtc) : "") +
                        (d.SmbiosGuid != null ? ", GUID " + d.SmbiosGuid : "") + (d.MacAddresses.Count > 0 ? ", MAC " + string.Join(" ", d.MacAddresses.ToArray()) : ""));
                foreach (PxeFlag f in m.Flags)
                    b.AppendLine("  PXE flag: owner " + f.Owner + ", deployment " + (f.DeploymentId ?? "") + (f.TimeUtc.HasValue ? ", offered " + Txt.Local(f.TimeUtc) : ""));
                if (m.Deployments.Length > 0) b.AppendLine("  Deployments: " + m.Deployments);
            }
            b.AppendLine();
            b.AppendLine("A deleted device loses its collection memberships, deployments, variables and history. PxeVice cannot bring it back.");
            return b.ToString();
        }

        static string FileSafe(string name)
        {
            StringBuilder b = new StringBuilder();
            foreach (char c in name ?? "")
                b.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            string s = b.ToString();
            return s.Length == 0 ? "machine" : (s.Length > 40 ? s.Substring(0, 40) : s);
        }

        // Writes the file and returns its path. Throws when it cannot, and then nothing is deleted.
        public static string Write(string folder, IList<Machine> machines, Plan plan, string ticket, string user, DateTime nowUtc)
        {
            Directory.CreateDirectory(folder);
            string first = machines.Count > 0 ? machines[0].Name : "reset";
            string name = nowUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + FileSafe(first) + (machines.Count > 1 ? "-and-" + (machines.Count - 1) + "-more" : "") + ".txt";
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, Text(machines, plan, ticket, user, nowUtc), new UTF8Encoding(false));
            if (!File.Exists(path)) throw new IOException("The backup file was not written.");
            return path;
        }
    }

    // How to start a network boot, by maker, for the person at the machine.
    public static class BootKeys
    {
        public sealed class Maker
        {
            public string Name, Key, Note;
        }

        public static readonly Maker[] All =
        {
            M("Dell", "F12", "Choose the network option, such as \u201cOnboard NIC (IPv4)\u201d."),
            M("HP", "F9 (or Esc, then F9)", "Choose \u201cNetwork Boot\u201d or \u201cIPv4 Network\u201d."),
            M("Lenovo", "F12 (or Enter, then F12)", "Choose \u201cPXE Network Boot\u201d or \u201cEthernet\u201d."),
            M("Microsoft Surface", "Hold the volume-down button while the Surface starts", "Needs the Surface Ethernet adapter. Release the button when the logo shows."),
            M("Hyper-V virtual machine", "Press any key when \u201cPress any key to boot from network\u201d shows", "A generation 2 machine needs the network adapter first in the boot order, or Start > Boot from network."),
            M("VMware virtual machine", "Esc at the VMware logo", "Choose \u201cNetwork boot from Intel E1000\u201d or the network adapter shown."),
            M("Other", "F12, F9 or Esc", "The key is on the first screen after the power button. Look for \u201cBoot menu\u201d."),
        };

        static Maker M(string name, string key, string note)
        {
            Maker m = new Maker();
            m.Name = name; m.Key = key; m.Note = note;
            return m;
        }

        public static Maker Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            // The maker as the firmware writes it: "Dell Inc.", "HP", "Hewlett-Packard", "LENOVO", "Microsoft Corporation".
            if (name.IndexOf("Hewlett", StringComparison.OrdinalIgnoreCase) >= 0) return All[1];
            if (name.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) < 0) return All[3];
            foreach (Maker m in All)
            {
                if (m.Name == "Other") continue;
                string first = m.Name.Split(' ')[0];
                if (name.IndexOf(first, StringComparison.OrdinalIgnoreCase) >= 0) return m;
            }
            return null;
        }

        // A message for the person at the machine, in plain words.
        public static string ForUser(string machineName, Maker maker)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("Hi, your computer " + machineName + " is ready to be installed again.");
            b.AppendLine();
            b.AppendLine("1. Plug in the network cable and turn the computer off.");
            if (maker != null)
            {
                b.AppendLine("2. Turn it on and press " + maker.Key + " again and again until a boot menu shows.");
                b.AppendLine("3. " + maker.Note);
            }
            else
            {
                b.AppendLine("2. Turn it on and press the boot menu key again and again until a boot menu shows. It is F12 on Dell and Lenovo, and F9 on HP.");
                b.AppendLine("3. Choose the network option, often called \u201cNetwork boot\u201d, \u201cPXE\u201d or \u201cIPv4\u201d.");
            }
            b.AppendLine("4. When it says \u201cPress F12 for network service boot\u201d or \u201cPress any key\u201d, press the key.");
            b.AppendLine();
            b.AppendLine("Tell me if the screen shows an error, and read me the message.");
            return b.ToString();
        }
    }

    // Resets of one machine in the activity log, to see a machine that keeps coming back.
    public static class ResetHistory
    {
        static readonly Regex Line = new Regex("<!\\[LOG\\[(.*?)\\]LOG\\]!><time=\"(\\d\\d:\\d\\d:\\d\\d)\\.\\d+[+-]\\d+\" date=\"(\\d\\d)-(\\d\\d)-(\\d{4})\"", RegexOptions.CultureInvariant);

        // The times (local) of the resets of a machine in the last days. Changes less than ten minutes apart are one reset.
        public static List<DateTime> Resets(IEnumerable<string> lines, string machine, DateTime nowLocal, int days)
        {
            List<DateTime> all = new List<DateTime>();
            string prefix = machine + ": ";
            foreach (string l in lines)
            {
                Match m = Line.Match(l);
                if (!m.Success) continue;
                string text = m.Groups[1].Value;
                if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || text.Contains("Failed: ")) continue;
                if (text.IndexOf("Clear the PXE flag", StringComparison.Ordinal) < 0 && text.IndexOf("Delete the device", StringComparison.Ordinal) < 0 && text.IndexOf("LastPXEAdvertisement", StringComparison.Ordinal) < 0) continue;
                DateTime t;
                if (!DateTime.TryParseExact(m.Groups[5].Value + "-" + m.Groups[3].Value + "-" + m.Groups[4].Value + " " + m.Groups[2].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                if (t < nowLocal.AddDays(-days) || t > nowLocal.AddMinutes(5)) continue;
                all.Add(t);
            }
            all.Sort();
            List<DateTime> resets = new List<DateTime>();
            foreach (DateTime t in all)
                if (resets.Count == 0 || (t - resets[resets.Count - 1]).TotalMinutes >= 10) resets.Add(t);
            return resets;
        }

        public static List<DateTime> ResetsInLog(string machine, DateTime nowLocal, int days)
        {
            List<string> lines = new List<string>();
            try
            {
                foreach (string p in new string[] { Path.Combine(ActivityLog.Folder, "PxeVice.lo_"), ActivityLog.FilePath })
                    if (File.Exists(p)) lines.AddRange(File.ReadAllLines(p));
            }
            catch (Exception) { }
            return Resets(lines, machine, nowLocal, days);
        }

        // A sentence for a machine that was reset more than once in the last two weeks, otherwise null.
        public static string Repeat(List<DateTime> resets)
        {
            if (resets == null || resets.Count < 2) return null;
            return "This machine was reset " + resets.Count + " times in the last 14 days, last " + resets[resets.Count - 1].ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                ". A reset that keeps being needed points at the cause: a task sequence that fails, a machine the site keeps registering again, or a deployment that is not there.";
        }
    }
}
