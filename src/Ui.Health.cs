namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Threading;
    using System.Windows.Forms;

    // The PXE health of the site: what has to be in place for any machine to PXE boot.
    public sealed class HealthDialog : PageForm
    {
        public HealthDialog(Theme t, List<HealthItem> items) : base("PXE health", t)
        {
            Size = new Size(Dpi.S(760), Dpi.S(700));
            int problems = Health.Problems(items);
            HealthState worst = Health.Worst(items);
            SettingRow head = new SettingRow(worst == HealthState.Problem ? Glyphs3.Warning : worst == HealthState.Ok ? Glyphs3.Accept : Glyphs.Info,
                worst == HealthState.Problem ? problems + (problems == 1 ? " thing stops PXE for every machine" : " things stop PXE for every machine") : worst == HealthState.Ok ? "Everything PxeVice can see is in place" : "Nothing is wrong, but some things are not known",
                worst == HealthState.Problem ? "Fix these first. A single machine cannot PXE boot while they are wrong." : worst == HealthState.Ok ? "These are the site-wide conditions for a PXE boot. A single machine can still be held back by a flag or a record, which Check shows." : "Items marked not known could not be read with this account. They are not counted as problems.", null);
            if (worst == HealthState.Problem) { head.Danger = true; head.RiskTag = "Problem"; }
            Add(head);
            Add(new SectionTitle("Checks"));
            foreach (HealthItem h in items)
            {
                string glyph = h.State == HealthState.Ok ? Glyphs3.Accept : h.State == HealthState.Problem ? Glyphs3.Warning : Glyphs.Info;
                SettingRow r = new SettingRow(glyph, h.Title, h.Detail + (h.State != HealthState.Ok && h.ToDo != null ? " " + h.ToDo : ""), null);
                if (h.State == HealthState.Problem) { r.Danger = true; r.RiskTag = "Problem"; }
                Add(r);
            }
            FlatButton copy = SmallButton("Copy as text", delegate { try { Clipboard.SetText(Health.AsText(items)); } catch (Exception) { } });
            Add(new SettingRow(Glyphs.Copy, "Copy the result", "As plain text, for a ticket or a message.", new ButtonStrip(copy)));
        }
    }

    public sealed partial class MainForm
    {
        void OpenHealth()
        {
            if (site == null || busy) return;
            ISite s = site;
            busy = true;
            Cursor = Cursors.AppStarting;
            Status("Checking the PXE setup of the site…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<HealthItem> items = null;
                Exception err = null;
                try { items = Health.Run(s, new CheckCache(), DateTime.UtcNow); }
                catch (Exception ex) { err = ex; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        busy = false;
                        Cursor = Cursors.Default;
                        if (err != null) { Status("The PXE health could not be checked: " + err.Message); return; }
                        Status(Health.Problems(items) == 0 ? "No problem found for the site as a whole." : Health.Problems(items) + " problem(s) found for the site as a whole.");
                        using (HealthDialog d = new HealthDialog(theme, items)) d.ShowDialog(this);
                    });
                }
                catch (Exception) { }
            });
        }
    }
}
