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
        readonly List<bool> risky = new List<bool>();
        public string EmptyText = "Nothing to change.";

        public StepList()
        {
            TabStop = false;
            SetStyle(ControlStyles.Selectable, false);
        }

        public void Set(IEnumerable<string> stepTexts, IEnumerable<string> noteTexts)
        {
            Set(stepTexts, null, noteTexts);
        }

        public void Set(IEnumerable<string> stepTexts, IEnumerable<bool> highRisk, IEnumerable<string> noteTexts)
        {
            steps.Clear();
            notes.Clear();
            risky.Clear();
            steps.AddRange(stepTexts);
            if (highRisk != null) risky.AddRange(highRisk);
            while (risky.Count < steps.Count) risky.Add(false);
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
                bool hot = risky[i];
                Shapes.Fill(g, badge, d / 2f, hot ? T.ErrBg : T.AccentSoft);
                Draw.Text(g, (i + 1).ToString(CultureInfo.InvariantCulture), Fonts.SmallSemibold, badge, hot ? T.ErrFg : T.Text, Draw.Line | TextFormatFlags.HorizontalCenter);
                int th = Draw.WrapHeight(steps[i], Fonts.Ui, tw);
                Draw.Text(g, steps[i], Fonts.Ui, new Rectangle(tx, y + Dpi.S(6) + Math.Max(0, (d - Fonts.Ui.Height) / 2), tw, th + 2), hot ? T.ErrFg : T.Text, Draw.Wrap);
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
        readonly TextBox ticket = new TextBox();

        // The ticket or reference written with the reset, cleaned.
        public string Ticket
        {
            get { return PxeVice.Ticket.Clean(ticket.Text); }
        }

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

            // A machine that keeps coming back needs more than another reset.
            foreach (Machine m in machines)
            {
                string again = ResetHistory.Repeat(ResetHistory.ResetsInLog(m.Name, DateTime.Now, 14));
                if (again != null) Add(new SettingRow(Glyphs.Info, "Reset before: " + m.Name, again, null));
            }

            ticket.Width = Dpi.S(220);
            ticket.Font = Fonts.Ui;
            ticket.BorderStyle = BorderStyle.FixedSingle;
            ticket.BackColor = t.Field;
            ticket.ForeColor = t.Text;
            ticket.MaxLength = PxeVice.Ticket.MaxLength;
            ticket.HandleCreated += delegate { MainForm.Cue(ticket, "INC0012345"); };
            ticket.TextChanged += Safe.Wrap(delegate { Refill(); });
            Add(new SectionTitle("Reference"));
            Add(new SettingRow(Glyphs.Log, PxePolicy.RequireTicket ? "Ticket (required)" : "Ticket", "A ticket number or a few words on why. It is written to the activity log with every change." + (PxePolicy.RequireTicket ? " Your organization asks for it." : ""), ticket));
            FlatButton bCopy = SmallButton("Copy for the user", delegate
            {
                ContextMenuStrip menu = new ContextMenuStrip();
                foreach (BootKeys.Maker mk in BootKeys.All)
                {
                    BootKeys.Maker one = mk;
                    menu.Items.Add(one.Name, null, delegate
                    {
                        try { Clipboard.SetText(BootKeys.ForUser(machines[0].Name, one)); } catch (Exception) { }
                    });
                }
                menu.Show(Cursor.Position);
            });
            Add(new SettingRow(Glyphs.Copy, "Tell the user how to boot from the network", "Copies a short message in plain words, with the boot key for the maker you choose.", new ButtonStrip(bCopy)));

            tUnknown.On = Prefs.GetBool("DeleteUnknown", true);
            tDevices.On = false;
            tRestart.On = Prefs.GetBool("RestartPxe", false);
            // A deleted device is still remembered by the PXE point, so deleting one turns the restart on. It can be turned off again.
            tDevices.Changed += Safe.Wrap(delegate { if (tDevices.On && !tRestart.On) tRestart.On = true; });
            foreach (Toggle x in new Toggle[] { tUnknown, tDevices, tRestart }) x.Changed += Safe.Wrap(delegate { Refill(); });
            Add(new SectionTitle("Options"));
            Add(new SettingRow(Glyphs3.Delete, "Delete Unknown device records", "Records named Unknown are left behind when a task sequence fails on an unknown computer. While one exists the machine is known, so it no longer gets deployments for Unknown Computers.", tUnknown));
            // A known device with no deployment never gets the deployment for Unknown Computers. Deleting it makes it unknown.
            List<string> noDeployment = new List<string>();
            foreach (Machine m in machines) if (m.HasNamedDevice && m.NotServiced) noDeployment.Add(m.Name);
            if (noDeployment.Count > 0)
            {
                string who = string.Join(", ", noDeployment.ToArray());
                Add(new SettingRow(Glyphs.Info, "No deployment reaches " + who, "The last PXE boot found no task sequence for " + (noDeployment.Count == 1 ? "this device" : "these devices") + ", because the site knows " + (noDeployment.Count == 1 ? "it" : "them") + " and nothing is deployed to " + (noDeployment.Count == 1 ? "it" : "them") + ". Either deploy a task sequence to a collection " + (noDeployment.Count == 1 ? "it is" : "they are") + " in, or delete the device below: then the PXE boot is handled as an unknown computer, by the deployment to All Unknown Computers. That deployment must exist.", null));
            }
            SettingRow danger = new SettingRow(Glyphs3.Delete, "Delete the device from ConfigMgr", "The device object is deleted from the site, so the next PXE boot is an unknown computer and gets the deployment for All Unknown Computers. It loses its collection memberships, deployments, variables and history, and the console shows it gone at once. A client that is still installed registers again later. PxeVice cannot bring it back. Turn it on only for the machine that should start over.", tDevices);
            danger.Danger = true;
            danger.RiskTag = "High risk";
            Add(danger);
            string pxe = info.PxeServers.Count == 0 ? "No PXE distribution point is known. Add one in Settings." : "Flushes the PXE cache on " + string.Join(", ", info.PxeServers.ToArray()) + ". Turned on with the deletion of a device, since the PXE point remembers it for a while. Otherwise only needed when SMSPXE.log still says the device is not serviced after a reset.";
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
                List<string> doomed = new List<string>();
                foreach (Step s in Chosen.Steps) if (s.HighRisk) doomed.Add(s.Target);
                if (doomed.Count > 0)
                {
                    string q = "Delete " + (doomed.Count == 1 ? "this device" : doomed.Count + " devices") + " from ConfigMgr?\n\n" + string.Join("\n", doomed.ToArray()) +
                        "\n\nThe device loses its collection memberships, deployments and variables. PxeVice cannot undo this.";
                    if (MessageBox.Show(this, q, "PxeVice: high risk", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                }
                Prefs.SetBool("DeleteUnknown", tUnknown.On);
                if (!tDevices.On) Prefs.SetBool("RestartPxe", tRestart.On);
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
            List<bool> hot = new List<bool>();
            foreach (Step s in p.Steps) { texts.Add(s.HighRisk ? "High risk: " + s.Text + ", with its collection memberships" : s.Text); hot.Add(s.HighRisk); }
            list.EmptyText = "Nothing is left to change for " + (machineCount == 1 ? "this machine." : "these machines.");
            List<string> notes = new List<string>(p.Notes);
            string why = p.Steps.Count > 0 ? PxePolicy.Blocked(p, machineCount, ticket.Text) : null;
            if (why != null) notes.Insert(0, why);
            list.Set(texts, hot, notes);
            bReset.Enabled = p.Steps.Count > 0 && why == null;
            bReset.Text = p.Steps.Count == 0 ? "Reset" : "Reset (" + p.Steps.Count + (p.Steps.Count == 1 ? " change)" : " changes)");
            bReset.AutoWidth();
            bReset.Width = Math.Max(bReset.Width, Dpi.S(120));
            if (IsHandleCreated) Relayout();
        }
    }

    // Asks for one line of text, such as the name of a site.
    public sealed class NameDialog : PageForm
    {
        readonly TextBox box = new TextBox();
        readonly Toggle remember = new Toggle();
        readonly bool secret;

        public string Value
        {
            get { return secret ? box.Text : box.Text.Trim(); }
        }

        public bool Remember
        {
            get { return remember.On; }
        }

        public NameDialog(Theme t, string title, string label, string desc, string start) : this(t, title, label, desc, start, false, null)
        {
        }

        // With secret the text is hidden, and with a remember text the dialog asks whether to keep it.
        public NameDialog(Theme t, string title, string label, string desc, string start, bool secret, string rememberText) : base(title, t)
        {
            this.secret = secret;
            Size = new Size(Dpi.S(640), Dpi.S(rememberText == null ? 300 : 400));
            box.UseSystemPasswordChar = secret;
            box.Width = Dpi.S(240);
            box.Font = Fonts.Ui;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = t.Field;
            box.ForeColor = t.Text;
            box.Text = start ?? "";
            Add(new SettingRow(secret ? Glyphs.Shield : Glyphs3.Server, label, desc, box));
            if (rememberText != null)
            {
                remember.On = true;
                Add(new SettingRow(Glyphs.Shield, "Remember the password", rememberText, remember));
            }
            FlatButton ok = new FlatButton(secret ? "Start" : "Save", secret ? Glyphs3.Play : Glyphs.Save);
            ok.Primary = true;
            ok.Dock = DockStyle.Right;
            ok.Width = Math.Max(ok.Width, Dpi.S(96));
            ok.ApplyTheme(t);
            ok.Click += delegate
            {
                if (Value.Length == 0) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            bClose.Text = "Cancel";
            bClose.Primary = false;
            bClose.ApplyTheme(t);
            Panel gap = new Panel();
            gap.Dock = DockStyle.Right;
            gap.Width = Dpi.S(8);
            footer.Controls.Add(gap);
            footer.Controls.Add(ok);
            footer.Controls.SetChildIndex(ok, 0);
            footer.Controls.SetChildIndex(gap, 1);
            AcceptButton = null;
            Shown += delegate { box.Focus(); box.SelectAll(); };
            box.KeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter || Value.Length == 0) return;
                e.SuppressKeyPress = true;
                DialogResult = DialogResult.OK;
                Close();
            };
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
            hero.Blurb = "Shows every PXE boot from SMSPXE.log and what it got, checks why a machine is not offered a task sequence, and clears what the site keeps after a PXE boot, such as PXE flags and Unknown device records, so the machine can PXE boot again. A companion to LogVice, PolicyVice and TrackVice.";
            hero.License = "Released under the MIT license.";
            Add(hero);
            Add(new SectionTitle("Command line"));
            KeyTable cli = new KeyTable(true, Cli.Help);
            Add(cli);
            Add(new SettingRow(Glyphs.Info, "Exit codes", "0 when done, 1 on an error, and 2 when nothing was found for the machine, or for /check when something blocks PXE. Use Start-Process with -Wait -PassThru in PowerShell.", null));
            Add(new SectionTitle("Keyboard shortcuts"));
            Add(new KeyTable(false,
                new string[] { "F5", "Read the site and SMSPXE.log again" },
                new string[] { "Ctrl+W", "Start or stop watching" },
                new string[] { "Ctrl+R", "Reset the selected machines" },
                new string[] { "Ctrl+E", "Report of the selected machines for the ticket" },
                new string[] { "Ctrl+F", "Search machines and PXE boots" },
                new string[] { "Ctrl+1 to Ctrl+4", "Machines, PXE boots, PXE flags, Activity" },
                new string[] { "Ctrl+L", "Open SMSPXE.log in LogVice" },
                new string[] { "Ctrl+C", "Copy the selected row" },
                new string[] { "Ctrl+,", "Settings" },
                new string[] { "F1", "This page" },
                new string[] { "Esc", "Clear the search, or close this page" }));
            Add(new SectionTitle("What PxeVice changes"));
            Add(new SettingRow(Glyphs.Shield, "Only on Reset, and only after you confirm", "Reading, watching, the PXE check and reports change nothing. A reset clears the PXE flag with Clear Required PXE Deployments, deletes Unknown device records, deletes the machine's rows in LastPXEAdvertisement and, when chosen, restarts the PXE service. Every change is written to " + ActivityLog.FilePath + ".", null));
            Add(new SettingRow(Glyphs2.Settings, "Permissions", "Rights in Configuration Manager to read devices, collections, deployments, task sequences, content status and the site's distribution points, and to delete devices. In the site database SELECT on v_R_System, v_Advertisement and v_Package, and SELECT and DELETE on LastPXEAdvertisement. Read access to SMSPXE.log on the PXE distribution points. Restarting the PXE service needs administrator rights on the distribution point.", null));

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
