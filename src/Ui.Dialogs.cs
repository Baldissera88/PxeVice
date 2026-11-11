namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.Text;
    using System.Windows.Forms;

    // A numbered list of the changes a reset makes, and notes below it.
    public sealed class StepList : FlatControl, IMeasured
    {
        readonly List<string> steps = new List<string>();
        readonly List<string> notes = new List<string>();
        public string EmptyText = "Nothing to change.";

        public StepList()
        {
            TabStop = false;
            SetStyle(ControlStyles.Selectable, false);
        }

        public void Set(IEnumerable<string> stepTexts, IEnumerable<string> noteTexts)
        {
            steps.Clear();
            notes.Clear();
            steps.AddRange(stepTexts);
            notes.AddRange(noteTexts);
            Invalidate();
        }

        int TextW(int width)
        {
            return Math.Max(Dpi.S(80), width - Dpi.S(64) - Dpi.S(20));
        }

        int RowH(string s, int width)
        {
            return Math.Max(Dpi.S(30), Draw.WrapHeight(s, Fonts.Ui, TextW(width)) + Dpi.S(12));
        }

        public int Measure(int width)
        {
            int h = Dpi.S(12);
            if (steps.Count == 0) h += RowH(EmptyText, width);
            foreach (string s in steps) h += RowH(s, width);
            foreach (string n in notes) h += RowH(n, width);
            return h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(T.Bg);
            Draw.Card(g, ClientRectangle, T);
            int y = Dpi.S(6), tx = Dpi.S(64), tw = TextW(Width);
            if (steps.Count == 0)
            {
                int h = RowH(EmptyText, Width);
                Draw.Text(g, EmptyText, Fonts.Ui, new Rectangle(tx, y + Dpi.S(6), tw, h), T.Text2, Draw.Wrap);
                y += h;
            }
            for (int i = 0; i < steps.Count; i++)
            {
                int h = RowH(steps[i], Width);
                int d = Dpi.S(22);
                Rectangle badge = new Rectangle(Dpi.S(24), y + Dpi.S(5), d, d);
                Shapes.Fill(g, badge, d / 2f, T.AccentSoft);
                Draw.Text(g, (i + 1).ToString(CultureInfo.InvariantCulture), Fonts.SmallSemibold, badge, T.Text, Draw.Line | TextFormatFlags.HorizontalCenter);
                int th = Draw.WrapHeight(steps[i], Fonts.Ui, tw);
                Draw.Text(g, steps[i], Fonts.Ui, new Rectangle(tx, y + Dpi.S(6) + Math.Max(0, (d - Fonts.Ui.Height) / 2), tw, th + 2), T.Text, Draw.Wrap);
                y += h;
            }
            foreach (string n in notes)
            {
                int h = RowH(n, Width);
                Rectangle gr = new Rectangle(Dpi.S(24), y + Dpi.S(5), Dpi.S(22), Dpi.S(22));
                if (Fonts.Icons != null) Draw.Glyph(g, Glyphs3.Warning, gr, T.WarnFg);
                int th = Draw.WrapHeight(n, Fonts.Ui, tw);
                Draw.Text(g, n, Fonts.Ui, new Rectangle(tx, y + Dpi.S(8), tw, th + 2), T.Text2, Draw.Wrap);
                y += h;
            }
        }
    }

    // Shows the machines, the options and every change before anything is changed.
    public sealed class ResetDialog : PageForm
    {
        readonly Plan full;
        readonly Toggle tUnknown = new Toggle();
        readonly Toggle tDevices = new Toggle();
        readonly Toggle tRestart = new Toggle();
        readonly StepList list = new StepList();
        readonly FlatButton bReset = new FlatButton("Reset", Glyphs2.Reset);
        readonly int machineCount;

        public ResetDialog(Theme t, IList<Machine> machines, Plan plan, SiteInfo info) : base(machines.Count == 1 ? "Reset " + machines[0].Name : "Reset " + machines.Count + " machines", t)
        {
            full = plan;
            machineCount = machines.Count;
            Size = new Size(Dpi.S(760), Dpi.S(720));

            Add(new SectionTitle(machines.Count == 1 ? "Machine" : "Machines"));
            foreach (Machine m in machines)
            {
                List<string> parts = new List<string>();
                if (m.MacAddress != null) parts.Add(m.MacAddress);
                if (m.SmbiosGuid != null) parts.Add(m.SmbiosGuid);
                parts.Add(m.Found);
                if (m.Deployments.Length > 0) parts.Add("deployment " + m.Deployments);
                SettingRow r = new SettingRow(Glyphs2.Device, m.Name, string.Join("  ·  ", parts.ToArray()), null);
                Add(r);
            }

            tUnknown.On = Prefs.GetBool("DeleteUnknown", true);
            tDevices.On = false;
            tRestart.On = Prefs.GetBool("RestartPxe", false);
            foreach (Toggle x in new Toggle[] { tUnknown, tDevices, tRestart }) x.Changed += Safe.Wrap(delegate { Refill(); });
            Add(new SectionTitle("Options"));
            Add(new SettingRow(Glyphs3.Delete, "Delete Unknown device records", "Records named Unknown are left behind when a task sequence fails on an unknown computer. While one exists the machine is known, so it no longer gets deployments for Unknown Computers.", tUnknown));
            Add(new SettingRow(Glyphs2.Device, "Delete named device records too", "Only when the machine should start over as a new computer. A deleted device loses its collection memberships, and comes back with its next discovery or client registration.", tDevices));
            string pxe = info.PxeServers.Count == 0 ? "No PXE distribution point is known. Add one in Settings." : "Flushes the PXE cache on " + string.Join(", ", info.PxeServers.ToArray()) + ". Needed only when SMSPXE.log still says the device is not serviced after a reset.";
            Add(new SettingRow(Glyphs3.Network, "Restart the PXE service afterwards", pxe, tRestart));

            Add(new SectionTitle("What PxeVice changes"));
            Add(list);
            SettingRow note = new SettingRow(Glyphs.Info, "About the site database", "PxeVice clears the flag with Clear Required PXE Deployments first, as the console does. Flags for unknown computers and deleted devices are not in the console, so PxeVice deletes those rows from LastPXEAdvertisement. Microsoft does not support direct changes to the site database, so only the rows of these machines are deleted.", null);
            Add(note);

            bClose.Text = "Cancel";
            bClose.Primary = false;
            bClose.AutoWidth();
            bClose.Width = Math.Max(bClose.Width, Dpi.S(96));
            bClose.ApplyTheme(t);
            bReset.Primary = true;
            bReset.Dock = DockStyle.Right;
            bReset.Width = Math.Max(bReset.Width, Dpi.S(120));
            bReset.ApplyTheme(t);
            bReset.Click += Safe.Wrap(delegate
            {
                Prefs.SetBool("DeleteUnknown", tUnknown.On);
                Prefs.SetBool("RestartPxe", tRestart.On);
                DialogResult = DialogResult.OK;
                Close();
            });
            Panel gap = new Panel();
            gap.Dock = DockStyle.Right;
            gap.Width = Dpi.S(8);
            footer.Controls.Add(gap);
            footer.Controls.Add(bReset);
            footer.Controls.SetChildIndex(bReset, 0);
            footer.Controls.SetChildIndex(gap, 1);
            Refill();
        }

        // The steps for the options that are on.
        public Plan Chosen
        {
            get
            {
                Plan p = new Plan();
                foreach (Step s in full.Steps)
                {
                    if (s.Kind == StepKind.DeleteUnknownRecord && !tUnknown.On) continue;
                    if (s.Kind == StepKind.DeleteDevice && !tDevices.On) continue;
                    if (s.Kind == StepKind.RestartService && !tRestart.On) continue;
                    p.Steps.Add(s);
                }
                // Restarting the PXE service only helps after something changed.
                if (p.Count(StepKind.RestartService) == p.Steps.Count) p.Steps.Clear();
                foreach (string n in full.Notes)
                {
                    if (!tRestart.On && (n.Contains("PXE service") || n.Contains("WDSServer") || n.Contains("PXE distribution point"))) continue;
                    p.Notes.Add(n);
                }
                return p;
            }
        }

        void Refill()
        {
            Plan p = Chosen;
            List<string> texts = new List<string>();
            foreach (Step s in p.Steps) texts.Add(s.Text);
            list.EmptyText = "Nothing is left to change for " + (machineCount == 1 ? "this machine." : "these machines.");
            list.Set(texts, p.Notes);
            bReset.Enabled = p.Steps.Count > 0;
            bReset.Text = p.Steps.Count == 0 ? "Reset" : "Reset (" + p.Steps.Count + (p.Steps.Count == 1 ? " change)" : " changes)");
            bReset.AutoWidth();
            bReset.Width = Math.Max(bReset.Width, Dpi.S(120));
            if (IsHandleCreated) Relayout();
        }
    }

    public sealed class SettingsDialog : PageForm
    {
        public event EventHandler ThemeChanged;
        public bool SiteChanged;
        readonly SettingRow logViceRow;

        public SettingsDialog(Theme t, SiteInfo info) : base("PxeVice settings", t)
        {
            string[] themes = { "System", "Light", "Dark", "Vice" };
            Segmented segTheme = new Segmented(themes);
            int ti = Array.IndexOf(themes, Prefs.Get("Theme", "System"));
            segTheme.Selected = ti < 0 ? 0 : ti;
            segTheme.Changed += Safe.Wrap(delegate
            {
                Prefs.Set("Theme", themes[segTheme.Selected]);
                Retheme(Theme.Named(themes[segTheme.Selected]));
                if (ThemeChanged != null) ThemeChanged(this, EventArgs.Empty);
            });

            Add(new SectionTitle("Appearance"));
            Add(new SettingRow(Glyphs2.Palette, "Theme", "Light, Dark or Vice, or follow the Windows app mode. The same themes as LogVice and PolicyVice.", segTheme));

            Add(new SectionTitle("Site"));
            Add(Field(Glyphs3.Server, "Site server", "The server with the SMS Provider. Leave it empty to use this computer.", "SiteServer", info != null ? info.SiteServer : null));
            Add(Field(Glyphs3.Server, "SQL Server", "Found from the site when empty. For a named instance, use SERVER\\INSTANCE.", "SqlServer", info != null ? info.SqlServer : null));
            Add(Field(Glyphs2.Code, "Site database", "Found from the site when empty, such as CM_P01.", "Database", info != null ? info.Database : null));
            Add(Field(Glyphs3.Network, "PXE distribution points", "Where the PXE service is restarted and SMSPXE.log is read. Separate several with commas. Found from the site when empty.", "PxeServers", info != null && info.PxeServers.Count > 0 ? string.Join(", ", info.PxeServers.ToArray()) : null));

            Toggle unknown = new Toggle();
            unknown.On = Prefs.GetBool("DeleteUnknown", true);
            unknown.Changed += Safe.Wrap(delegate { Prefs.SetBool("DeleteUnknown", unknown.On); });
            Toggle restart = new Toggle();
            restart.On = Prefs.GetBool("RestartPxe", false);
            restart.Changed += Safe.Wrap(delegate { Prefs.SetBool("RestartPxe", restart.On); });
            Add(new SectionTitle("Reset"));
            Add(new SettingRow(Glyphs3.Delete, "Delete Unknown device records", "Turned on when the reset page opens. Named device records are never deleted unless you turn that on for the reset.", unknown));
            Add(new SettingRow(Glyphs3.Network, "Restart the PXE service", "Turned on when the reset page opens.", restart));

            FlatButton bBrowse = SmallButton("Browse", delegate { Browse(); });
            FlatButton bClear = SmallButton("Clear", delegate
            {
                Prefs.Set("LogVicePath", null);
                UpdateLogVice();
            });
            logViceRow = new SettingRow(Glyphs2.Folder, "LogVice", "", new ButtonStrip(bBrowse, bClear));
            Add(new SectionTitle("LogVice"));
            Add(logViceRow);
            UpdateLogVice();

            Add(new SectionTitle("Data"));
            SettingRow store = new SettingRow(Glyphs2.Settings, "Where settings are stored", Prefs.Location, null);
            store.MonoDesc = true;
            Add(store);
            FlatButton bFolder = SmallButton("Open folder", delegate { MainForm.OpenPath(ActivityLog.Folder); });
            SettingRow log = new SettingRow(Glyphs3.History, "Activity log", ActivityLog.FilePath + "\nEvery change PxeVice makes, with who made it, in the CMTrace format.", new ButtonStrip(bFolder));
            log.MonoDesc = true;
            Add(log);
        }

        SettingRow Field(string glyph, string title, string desc, string pref, string found)
        {
            TextBox box = new TextBox();
            box.Width = Dpi.S(260);
            box.Font = Fonts.Ui;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = theme.Field;
            box.ForeColor = theme.Text;
            box.Text = Prefs.Get(pref, "");
            if (box.Text.Length == 0 && !string.IsNullOrEmpty(found)) box.HandleCreated += delegate { MainForm.Cue(box, found); };
            box.TextChanged += Safe.Wrap(delegate
            {
                string v = box.Text.Trim();
                Prefs.Set(pref, v.Length == 0 ? null : v);
                SiteChanged = true;
            });
            return new SettingRow(glyph, title, desc, box);
        }

        void UpdateLogVice()
        {
            string custom = Prefs.Get("LogVicePath", null);
            string found = LogViceLink.Find();
            if (!string.IsNullOrEmpty(custom)) logViceRow.Desc = "Chosen: " + custom + (found == custom ? "" : " (not found)");
            else if (found != null) logViceRow.Desc = "Found: " + found;
            else logViceRow.Desc = "Not found. PxeVice looks for the LogVice MSI install, App Paths and LogVice.exe next to PxeVice.exe. Browse to choose it.";
            if (IsHandleCreated) Relayout();
        }

        void Browse()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "LogVice|LogVice.exe|Programs|*.exe";
                d.Title = "Locate LogVice";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Prefs.Set("LogVicePath", d.FileName);
            }
            UpdateLogVice();
        }
    }

    public sealed class AboutDialog : PageForm
    {
        public AboutDialog(Theme t) : base("About PxeVice", t)
        {
            AboutHero hero = new AboutHero();
            hero.Version = "Version " + Cli.Version;
            hero.Detail = "Built " + Program.BuildDate;
            hero.Developer = Program.Developer;
            hero.Blurb = "Finds what a Configuration Manager site keeps after a PXE boot, such as PXE flags, Unknown device records and flags of deleted devices, and clears them so the machine can PXE boot again. A companion to LogVice and PolicyVice.";
            hero.License = "Released under the MIT license.";
            Add(hero);
            Add(new SectionTitle("Command line"));
            KeyTable cli = new KeyTable(true, Cli.Help);
            Add(cli);
            Add(new SettingRow(Glyphs.Info, "Exit codes", "0 when done, 1 on an error, and 2 when nothing was found for the machine. Use Start-Process with -Wait in PowerShell.", null));
            Add(new SectionTitle("Keyboard shortcuts"));
            Add(new KeyTable(false,
                new string[] { "F5", "Read the site again" },
                new string[] { "Ctrl+R", "Reset the selected machines" },
                new string[] { "Ctrl+F", "Search machines" },
                new string[] { "Ctrl+L", "Open SMSPXE.log in LogVice" },
                new string[] { "Ctrl+C", "Copy the selected row" },
                new string[] { "Ctrl+,", "Settings" },
                new string[] { "F1", "This page" },
                new string[] { "Esc", "Clear the search, or close this page" }));
            Add(new SectionTitle("What PxeVice changes"));
            Add(new SettingRow(Glyphs.Shield, "Only on Reset, and only after you confirm", "Reading changes nothing. A reset clears the PXE flag with Clear Required PXE Deployments, deletes Unknown device records, deletes the machine's rows in LastPXEAdvertisement and, when chosen, restarts the PXE service. Every change is written to " + ActivityLog.FilePath + ".", null));
            Add(new SettingRow(Glyphs2.Settings, "Permissions", "Rights in Configuration Manager to read and delete devices, and in the site database SELECT on v_R_System, v_Advertisement and v_Package, and SELECT and DELETE on LastPXEAdvertisement. Restarting the PXE service needs administrator rights on the distribution point.", null));

            FlatButton bCopy = new FlatButton("Copy details", Glyphs.Copy);
            bCopy.Dock = DockStyle.Left;
            bCopy.ApplyTheme(t);
            bCopy.Click += Safe.Wrap(delegate
            {
                StringBuilder b = new StringBuilder();
                b.Append("PxeVice ").Append(Cli.Version).Append(" (built ").Append(Program.BuildDate).Append(") by ").Append(Program.Developer).Append("\r\n");
                b.Append("Windows: ").Append(Program.OsVersion()).Append("\r\n");
                b.Append(".NET: ").Append(Environment.Version).Append(Environment.Is64BitProcess ? ", 64-bit" : ", 32-bit").Append("\r\n");
                b.Append("User: ").Append(Cli.UserName).Append(Program.IsAdmin ? " (administrator)" : "").Append("\r\n");
                b.Append("Program: ").Append(Program.Location).Append("\r\n\r\n").Append(cli.AsText());
                bCopy.Text = Menus.Copy(b.ToString()) ? "Copied" : "Clipboard busy";
            });
            footer.Controls.Add(bCopy);
        }
    }
}
