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
            // A deleted device is still remembered by the PXE point, so deleting one turns the restart on. It can be turned off again.
            tDevices.Changed += Safe.Wrap(delegate { if (tDevices.On && !tRestart.On) tRestart.On = true; });
            foreach (Toggle x in new Toggle[] { tUnknown, tDevices, tRestart }) x.Changed += Safe.Wrap(delegate { Refill(); });
            Add(new SectionTitle("Options"));
            Add(new SettingRow(Glyphs3.Delete, "Delete Unknown device records", "Records named Unknown are left behind when a task sequence fails on an unknown computer. While one exists the machine is known, so it no longer gets deployments for Unknown Computers.", tUnknown));
            Add(new SettingRow(Glyphs2.Device, "Delete named device records too", "Only when the machine should start over as a new computer. A deleted device loses its collection memberships, and comes back with its next discovery or client registration. The site marks it decommissioned: it is gone from the console at once, and removed from the database later.", tDevices));
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
        public bool UseAccountNow;
        List<string> found;

        string FoundText()
        {
            string what = "Every distribution point with PXE turned on, from the site. PxeVice reads SMSPXE.log on all of them at the same time.";
            if (found == null) return what;
            if (found.Count == 0) return what + " None was found on this site.";
            return what + " Found " + found.Count + ": " + string.Join(", ", found.ToArray()) + ".";
        }

        static string PxeDesc(bool find)
        {
            return find ? "Added to the ones found, such as a server the site does not list. Separate several with commas." : "PxeVice uses only these. Separate several with commas.";
        }
        static string AccountState()
        {
            string now = RunAs.Account != null ? "PxeVice runs with " + RunAs.Account + " on the network now. " : "PxeVice runs with your own account now. ";
            if (string.IsNullOrEmpty(Prefs.Get(RunAs.UserPref, null))) return now + "Write an account above to use it.";
            return now + (Secret.Load(RunAs.PasswordPref).Length > 0 ? "The password is remembered for your Windows user, encrypted with DPAPI." : "PxeVice asks for the password when it opens.") + " Use it now asks for the password and connects again with the account.";
        }
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
            Toggle find = new Toggle();
            find.On = Prefs.GetBool("FindPxeServers", true);
            SettingRow pxeRow = null;
            find.Changed += Safe.Wrap(delegate
            {
                Prefs.SetBool("FindPxeServers", find.On);
                SiteChanged = true;
                pxeRow.Title = find.On ? "More PXE distribution points" : "Only these PXE distribution points";
                pxeRow.Desc = PxeDesc(find.On);
                Relayout();
            });
            found = info == null ? null : info.FoundPxeServers;
            Add(new SettingRow(Glyphs3.Network, "Find the PXE distribution points from the site", FoundText(), find));
            pxeRow = Field(Glyphs3.Network, find.On ? "More PXE distribution points" : "Only these PXE distribution points", PxeDesc(find.On), "PxeServers", null);
            Add(pxeRow);
            Add(Field(Glyphs.Log, "SMSPXE.log", "Only when PxeVice does not find it. <server> stands for each PXE distribution point, such as \\\\<server>\\D$\\SMS\\Logs\\SMSPXE.log.", "PxeLogPath", null));

            Add(new SectionTitle("Service account"));
            TextBox account = new TextBox();
            account.Width = Dpi.S(260);
            account.Font = Fonts.Ui;
            account.BorderStyle = BorderStyle.FixedSingle;
            account.BackColor = theme.Field;
            account.ForeColor = theme.Text;
            account.Text = Prefs.Get(RunAs.UserPref, "");
            account.HandleCreated += delegate { MainForm.Cue(account, "DOMAIN\\svc-pxevice"); };
            account.TextChanged += Safe.Wrap(delegate
            {
                string v = account.Text.Trim();
                if (v != Prefs.Get(RunAs.UserPref, "")) Secret.Save(RunAs.PasswordPref, null);
                Prefs.Set(RunAs.UserPref, v.Length == 0 ? null : v);
            });
            Add(new SettingRow(Glyphs.Shield, "Account", "PxeVice connects to the site, the site database and the PXE distribution points with this account, like runas /netonly. On this PC you are still yourself, and the activity log says both. Leave it empty to use your own account.", account));
            SettingRow pw = null;
            FlatButton bForget = SmallButton("Forget the password", delegate
            {
                Secret.Save(RunAs.PasswordPref, null);
                pw.Desc = AccountState();
                Relayout();
            });
            FlatButton bRestart = SmallButton("Use it now", delegate
            {
                UseAccountNow = true;
                Close();
            });
            pw = new SettingRow(Glyphs3.Play, "Use the account", AccountState(), new ButtonStrip(bRestart, bForget));
            Add(pw);

            List<SavedSite> saved = Sites.All();
            if (saved.Count > 0)
            {
                Add(new SectionTitle("Saved sites"));
                foreach (SavedSite site in saved)
                {
                    SavedSite one = site;
                    SettingRow row = null;
                    FlatButton bRemove = SmallButton("Remove", delegate
                    {
                        Sites.Remove(one.Name);
                        row.Visible = false;
                        Relayout();
                    });
                    row = new SettingRow(Glyphs3.Server, site.Name, site.Describe() + (string.IsNullOrEmpty(site.PxeServers) ? "" : " · PXE: " + site.PxeServers) + ". Switch with Site in the toolbar.", new ButtonStrip(bRemove));
                    Add(row);
                }
            }

            string[] secs = { "5", "10", "30", "60" };
            Segmented segWatch = new Segmented(new string[] { "5 s", "10 s", "30 s", "60 s" });
            int wi = Array.IndexOf(secs, Prefs.GetInt("WatchSeconds", 10).ToString(System.Globalization.CultureInfo.InvariantCulture));
            segWatch.Selected = wi < 0 ? 1 : wi;
            segWatch.Changed += Safe.Wrap(delegate { Prefs.Set("WatchSeconds", secs[segWatch.Selected]); });
            Toggle afterReset = new Toggle();
            afterReset.On = Prefs.GetBool("WatchAfterReset", true);
            afterReset.Changed += Safe.Wrap(delegate { Prefs.SetBool("WatchAfterReset", afterReset.On); });
            Toggle onStart = new Toggle();
            onStart.On = Prefs.GetBool("WatchOnStart", false);
            onStart.Changed += Safe.Wrap(delegate { Prefs.SetBool("WatchOnStart", onStart.On); });
            string[] hours = { "1", "4", "24", "72" };
            Segmented segHours = new Segmented(new string[] { "1 h", "4 h", "24 h", "3 days" });
            int hi = Array.IndexOf(hours, Prefs.GetInt("RequestHours", 24).ToString(System.Globalization.CultureInfo.InvariantCulture));
            segHours.Selected = hi < 0 ? 2 : hi;
            segHours.Changed += Safe.Wrap(delegate { Prefs.Set("RequestHours", hours[segHours.Selected]); });
            Add(new SectionTitle("Watch and PXE boots"));
            Add(new SettingRow(Glyphs.Watch, "Read every", "How often Watch reads the site and the new lines of SMSPXE.log.", segWatch));
            Add(new SettingRow(Glyphs.Watch, "Watch after a reset", "Start watching after a reset, so PxeVice tells what the machine gets at its next PXE boot.", afterReset));
            Add(new SettingRow(Glyphs.Watch, "Watch when PxeVice opens", "Start watching at once.", onStart));
            Add(new SettingRow(Glyphs3.History, "PXE boots from the last", "The PXE boots tab, and machines listed only for a PXE boot that was not serviced. PxeVice reads up to 8 MB of SMSPXE.log and SMSPXE.lo_ when it connects.", segHours));

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
