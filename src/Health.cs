namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public enum HealthState { Ok, Problem, Unknown }

    public sealed class HealthItem
    {
        public HealthState State;
        public string Title, Detail, ToDo;
    }

    // What decides whether any machine can PXE boot at all, for the whole site and not one machine.
    public static class Health
    {
        public const string AllUnknownComputers = "SMS000US";

        static HealthItem Make(HealthState state, string title, string detail, string todo)
        {
            HealthItem h = new HealthItem();
            h.State = state; h.Title = title; h.Detail = detail; h.ToDo = todo;
            return h;
        }

        public static List<HealthItem> Run(ISite site, CheckCache cache, DateTime now)
        {
            List<HealthItem> l = new List<HealthItem>();
            cache.Load(site);
            List<string> servers = site.Info.PxeServers;

            if (servers.Count == 0)
                l.Add(Make(HealthState.Problem, "PXE distribution point", "No distribution point with PXE turned on was found.", "Turn on PXE on a distribution point, or add the server under Site in Settings."));
            else
                l.Add(Make(HealthState.Ok, "PXE distribution point", servers.Count + (servers.Count == 1 ? " point: " : " points: ") + string.Join(", ", servers.ToArray()) + ".", null));

            foreach (string server in servers)
            {
                try
                {
                    List<string> running = site.RunningPxeServices(server);
                    if (running.Count > 0) l.Add(Make(HealthState.Ok, "PXE service on " + server, string.Join(", ", running.ToArray()) + " is running.", null));
                    else l.Add(Make(HealthState.Problem, "PXE service on " + server, "Neither WDSServer nor SccmPxe is running.", "Start the service on " + server + ", or restart it from a reset."));
                }
                catch (Exception ex) { l.Add(Make(HealthState.Unknown, "PXE service on " + server, "Not known: " + ex.Message, "Check that your account is administrator on " + server + ", or use a service account.")); }

                PxePoint pp = null;
                if (cache.Points != null) foreach (PxePoint p in cache.Points) if (Checker.SameServer(p.Server, server)) pp = p;
                if (pp == null) l.Add(Make(HealthState.Unknown, "Unknown computer support on " + server, cache.PointsError != null ? "Not known: " + cache.PointsError : "Not known: the site does not list this point.", null));
                else if (pp.SupportsUnknown == null) l.Add(Make(HealthState.Unknown, "Unknown computer support on " + server, "Not known: the site does not say.", null));
                else if (pp.SupportsUnknown == false) l.Add(Make(HealthState.Problem, "Unknown computer support on " + server, "The point does not answer unknown computers.", "Turn on “Enable unknown computer support” in the PXE settings of the distribution point."));
                else l.Add(Make(HealthState.Ok, "Unknown computer support on " + server, "The point answers unknown computers.", null));

                try
                {
                    string path = site.PxeLog(server);
                    if (path == null) l.Add(Make(HealthState.Unknown, "SMSPXE.log on " + server, "Not known: PxeVice did not find the log.", "Set the path under Site in Settings."));
                    else l.Add(Make(HealthState.Ok, "SMSPXE.log on " + server, "PxeVice can read the log.", null));
                }
                catch (Exception ex) { l.Add(Make(HealthState.Unknown, "SMSPXE.log on " + server, "Not known: " + ex.Message, "Set the path under Site in Settings.")); }
            }

            if (cache.Deployments == null)
            {
                l.Add(Make(HealthState.Unknown, "Deployment to All Unknown Computers", "Not known: " + (cache.DeploymentsError ?? "the deployments could not be read."), null));
                return l;
            }
            List<TsDeployment> unknown = new List<TsDeployment>();
            foreach (TsDeployment d in cache.Deployments)
                if (string.Equals(d.CollectionId, AllUnknownComputers, StringComparison.OrdinalIgnoreCase) && d.ForPxe && !d.Expired(now) && !d.NotYet(now)) unknown.Add(d);
            if (unknown.Count == 0)
                l.Add(Make(HealthState.Problem, "Deployment to All Unknown Computers", "No task sequence is deployed to All Unknown Computers and available to media and PXE. A machine the site does not know gets nothing.", "Deploy a task sequence to All Unknown Computers and make it available to “Configuration Manager clients, media and PXE”."));
            else
            {
                List<string> names = new List<string>();
                foreach (TsDeployment d in unknown) names.Add(d.Text);
                l.Add(Make(HealthState.Ok, "Deployment to All Unknown Computers", string.Join("; ", names.ToArray()) + ".", null));
            }

            List<string> boot = new List<string>();
            foreach (TsDeployment d in unknown) if (!string.IsNullOrEmpty(d.BootImageId) && !boot.Contains(d.BootImageId)) boot.Add(d.BootImageId);
            foreach (string id in boot)
            {
                try
                {
                    List<string> on = cache.ContentOn(site, id);
                    List<string> missing = new List<string>();
                    foreach (string server in servers)
                    {
                        bool has = false;
                        foreach (string o in on) if (Checker.SameServer(o, server)) has = true;
                        if (!has) missing.Add(server);
                    }
                    if (missing.Count == 0) l.Add(Make(HealthState.Ok, "Boot image " + id, "The content is on every PXE point.", null));
                    else l.Add(Make(HealthState.Problem, "Boot image " + id, "The content is missing on " + string.Join(", ", missing.ToArray()) + ", so a machine that boots from the network there cannot load WinPE.", "Distribute the boot image to " + string.Join(", ", missing.ToArray()) + "."));
                }
                catch (Exception ex) { l.Add(Make(HealthState.Unknown, "Boot image " + id, "Not known: " + ex.Message, null)); }
            }
            if (unknown.Count > 0 && boot.Count == 0)
                l.Add(Make(HealthState.Unknown, "Boot image", "Not known: the deployment does not name a boot image.", null));
            return l;
        }

        public static HealthState Worst(List<HealthItem> items)
        {
            HealthState w = HealthState.Ok;
            foreach (HealthItem h in items)
            {
                if (h.State == HealthState.Problem) return HealthState.Problem;
                if (h.State == HealthState.Unknown) w = HealthState.Unknown;
            }
            return w;
        }

        public static int Problems(List<HealthItem> items)
        {
            int n = 0;
            foreach (HealthItem h in items) if (h.State == HealthState.Problem) n++;
            return n;
        }

        public static string Mark(HealthState s)
        {
            return s == HealthState.Ok ? "OK     " : s == HealthState.Problem ? "PROBLEM" : "UNKNOWN";
        }

        public static string AsText(List<HealthItem> items)
        {
            StringBuilder b = new StringBuilder();
            foreach (HealthItem h in items)
            {
                b.AppendLine(Mark(h.State) + "  " + h.Title + ": " + h.Detail);
                if (h.State != HealthState.Ok && h.ToDo != null) b.AppendLine("         To do: " + h.ToDo);
            }
            return b.ToString();
        }
    }
}
