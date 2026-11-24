namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public enum Verdict
    {
        Good,
        Waiting,
        Problem
    }

    public enum FixBy
    {
        Nobody,
        Reset,
        Admin
    }

    // One line of how a PXE boot went.
    public sealed class TrailStep
    {
        // True: it went well. False: it stopped here. Null: not known yet.
        public bool? Ok;
        public string Text;
    }

    // The check in short, in plain words for someone who does not know ConfigMgr: why the machine boots from the network,
    // what stops the installation, who can fix it and what to do.
    public sealed class Summary
    {
        public Verdict Verdict;
        public string Headline;
        public string Why;
        public string Stops;
        public FixBy Fix;
        public string Next;
        public readonly List<TrailStep> Trail = new List<TrailStep>();

        public string FixText
        {
            get
            {
                switch (Fix)
                {
                    case FixBy.Reset: return "You can fix this yourself with Reset.";
                    case FixBy.Admin: return "This needs a ConfigMgr administrator.";
                }
                return null;
            }
        }

        // Titles of the findings, as Checker writes them, as short keys.
        static string KeyOf(Finding f)
        {
            string t = f.Title ?? "";
            if (t == "Required deployment already offered" || t == "PXE flag") return "flag";
            if (t == "Leftover Unknown device record") return "unknownrecord";
            if (t == "Last PXE boot was not serviced") return "notserviced";
            if (t == "No task sequence is deployed to it") return "nodeployment";
            if (t == "Deployment not available to PXE") return "notpxe";
            if (t == "Deployment not available yet") return "notyet";
            if (t == "Deployment expired") return "expired";
            if (t == "Task sequence without a boot image") return "noboot";
            if (t == "Boot image not on the PXE distribution point") return "bootmissing";
            if (t == "Unknown computer support is off") return "unknownoff";
            if (t.StartsWith("Duplicate ")) return "duplicate";
            if (t == "Several device records") return "several";
            return "";
        }

        static void Say(Summary s, string stops, FixBy fix, string next)
        {
            s.Stops = stops;
            s.Fix = fix;
            s.Next = next;
        }

        // What stops the installation, in plain words, for the most important finding.
        static void Explain(Summary s, Finding f, Machine m)
        {
            string todo = f.ToDo ?? "";
            switch (KeyOf(f))
            {
                case "flag":
                    Say(s, "The site has a note that this machine already received its installation, so it does not offer it again.", FixBy.Reset, "Press Reset. It removes the note.");
                    break;
                case "unknownrecord":
                    Say(s, "An earlier installation failed halfway and left a half-made record behind, so the site no longer treats this machine as a new computer.", FixBy.Reset, "Press Reset. It deletes the record.");
                    break;
                case "nodeployment":
                    if (todo.StartsWith("Deploy a task sequence"))
                        Say(s, "No installation is set up for new computers, and this machine is treated as a new computer.", FixBy.Admin, "Ask a ConfigMgr administrator to deploy a task sequence to All Unknown Computers, available to PXE.");
                    else if (todo.StartsWith("Reset the machine"))
                        Say(s, "An earlier installation failed halfway and left a half-made record behind, so the site no longer treats this machine as a new computer.", FixBy.Reset, "Press Reset. It deletes the record.");
                    else
                        Say(s, "The site knows this computer, but no installation is set up for it.", FixBy.Admin, "A ConfigMgr administrator can add it to a collection with an installation. Or press Reset and turn on Delete the device (high risk), so it counts as a new computer.");
                    break;
                case "notpxe":
                    Say(s, "An installation exists for it, but it is not allowed to start from the network.", FixBy.Admin, "Ask a ConfigMgr administrator to make the deployment available to media and PXE.");
                    break;
                case "notyet":
                    Say(s, "The installation is not open yet.", FixBy.Admin, "Wait, or ask a ConfigMgr administrator to change its start time.");
                    break;
                case "expired":
                    Say(s, "The installation has ended.", FixBy.Admin, "Ask a ConfigMgr administrator to change or remove its end time.");
                    break;
                case "noboot":
                    Say(s, "The installation has no startup image, so it cannot start from the network.", FixBy.Admin, "Ask a ConfigMgr administrator to set a boot image on the task sequence.");
                    break;
                case "bootmissing":
                    Say(s, "The startup image the installation needs is not on the PXE server.", FixBy.Admin, "Ask a ConfigMgr administrator to distribute the boot image to the PXE server.");
                    break;
                case "unknownoff":
                    Say(s, "The PXE server is set not to answer new computers, and this machine is a new computer to it.", FixBy.Admin, "Ask a ConfigMgr administrator to turn on unknown computer support on the PXE server, or to import the computer.");
                    break;
                default:
                    Say(s, f.Title + ".", FixBy.Nobody, f.ToDo);
                    break;
            }
        }

        static string Reason(Machine m, PxeRequest last, List<Finding> findings)
        {
            bool required = findings.Exists(delegate (Finding f) { return f.Title == "Required deployment already offered"; });
            if (required)
            {
                foreach (PxeFlag f in m.Flags)
                    return "An installation is waiting for it: " + m.DeploymentText(f.DeploymentId) + " is required, so the machine is meant to be installed again.";
            }
            if (last != null && last.Served && last.Mandatory == true)
                return m.DeploymentText(last.OfferId) + " is required for it, so every start from the network begins it.";
            if (last != null && last.Served && last.Mandatory == false)
                return m.DeploymentText(last.OfferId) + " is available to it, so someone at the machine chose to start from the network (F12) and picked it.";
            if (last != null && last.IsUnknownComputer)
                return "The site does not know this computer, so it treats it as a new one. New computers only get the installation meant for new computers.";
            return "The computer started from the network, probably because it has no working Windows or someone chose to. The site cannot tell which.";
        }

        static string Known(Machine m, PxeRequest last)
        {
            if (last.IsUnknownComputer) return "The site does not know it, so it is a new computer (an Unknown Computer).";
            foreach (DeviceRecord d in m.Devices)
                if (d.ResourceId == last.ItemKey) return d.IsUnknownRecord ? "The site knows it only as a leftover record named Unknown." : "The site knows it as the device " + d.Name + ".";
            return "The site knows it as the device " + m.Name + ".";
        }

        static void BuildTrail(Summary s, Machine m, DateTime now)
        {
            PxeRequest last = m.LastRequest;
            if (last == null)
            {
                s.Trail.Add(new TrailStep { Ok = null, Text = "No PXE boot of this machine is in SMSPXE.log yet." });
                return;
            }
            string where = string.IsNullOrEmpty(last.Server) ? "" : " " + last.Server;
            s.Trail.Add(new TrailStep { Ok = true, Text = "The machine asked the network for an installation, " + Txt.Ago(last.Utc, now) + (where.Length > 0 ? ", on" + where : "") + "." });
            bool leftover = false;
            foreach (DeviceRecord d in m.Devices) if (d.ResourceId == last.ItemKey && d.IsUnknownRecord) leftover = true;
            s.Trail.Add(new TrailStep { Ok = leftover ? (bool?)false : (last.IsUnknownComputer ? null : (bool?)true), Text = Known(m, last) });
            if (last.Served)
            {
                s.Trail.Add(new TrailStep { Ok = true, Text = "It found an installation for it: " + m.DeploymentText(last.OfferId) + (last.Mandatory == true ? ", required." : last.Mandatory == false ? ", available." : ".") });
                s.Trail.Add(new TrailStep { Ok = true, Text = "The PXE server offered it, so the machine can install." });
            }
            else if (last.Outcome == RequestOutcome.Open)
            {
                s.Trail.Add(new TrailStep { Ok = null, Text = "SMSPXE.log has no answer yet." });
            }
            else
            {
                s.Trail.Add(new TrailStep { Ok = false, Text = "It found no installation for it." });
                s.Trail.Add(new TrailStep { Ok = false, Text = "Nothing was offered, so the machine started from its disk instead." });
            }
        }

        public static Summary From(Machine m, List<Finding> findings, DateTime now)
        {
            Summary s = new Summary();
            PxeRequest last = m.LastRequest;
            s.Why = Reason(m, last, findings);
            BuildTrail(s, m, now);

            // The symptom (the last boot was not serviced) is not a cause. The causes are the other findings that block.
            Finding cause = null;
            bool symptom = false;
            foreach (Finding f in findings)
            {
                if (f.Severity != Severity.Blocks) continue;
                if (KeyOf(f) == "notserviced") { symptom = true; continue; }
                if (cause == null) cause = f;
            }
            if (cause != null)
            {
                s.Verdict = Verdict.Problem;
                s.Headline = "It will not get an installation when it starts from the network";
                Explain(s, cause, m);
                // A reset also fixes the other blocking causes that it can fix, so say so when there are two.
            }
            else if (symptom)
            {
                s.Verdict = Verdict.Problem;
                s.Headline = "Its last start from the network got no installation";
                Say(s, "The check found no clear reason. The log lines under PXE check show what the PXE server answered.", FixBy.Nobody, "Press Reset, boot the machine from the network again, and watch what it gets.");
            }
            else if (last != null && last.Served)
            {
                s.Verdict = Verdict.Good;
                s.Headline = "It was offered an installation on its last start from the network";
                s.Next = last.Mandatory == false ? "The installation is available, so press F12 when asked and choose it." : null;
            }
            else
            {
                s.Verdict = Verdict.Waiting;
                s.Headline = "Nothing in the site stops it from getting an installation";
                s.Next = "Start the machine from the network and watch what it gets.";
            }
            return s;
        }

        public static string AsText(Summary s)
        {
            StringBuilder b = new StringBuilder();
            b.Append("In short: ").Append(s.Headline).Append(".\r\n");
            b.Append("  Why it starts from the network: ").Append(s.Why).Append("\r\n");
            if (s.Stops != null) b.Append("  What stops it: ").Append(s.Stops).Append("\r\n");
            if (s.FixText != null) b.Append("  Who can fix it: ").Append(s.FixText).Append("\r\n");
            if (s.Next != null) b.Append("  What to do: ").Append(s.Next).Append("\r\n");
            b.Append("  How the PXE boot went:\r\n");
            foreach (TrailStep t in s.Trail) b.Append("    ").Append(t.Ok == true ? "OK       " : t.Ok == false ? "STOPPED  " : "-        ").Append(t.Text).Append("\r\n");
            return b.ToString();
        }
    }
}
