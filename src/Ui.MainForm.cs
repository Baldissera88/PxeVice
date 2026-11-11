namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Windows.Forms;

    public sealed class MainForm : Form
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const int TabMachines = 0, TabFlags = 1, TabActivity = 2;
        const string FilterUnknown = "unknown", FilterRecords = "records", FilterDeleted = "deleted", FilterRecent = "recent";

        Theme theme;
        readonly bool demo;
        bool busy, pending;
        ISite site;
        SiteData data;
        readonly List<Activity> activities = new List<Activity>();
        string chipFilter;

        readonly BusyBar busyBar = new BusyBar();
        readonly List<IThemed> themed = new List<IThemed>();
        readonly Panel header = new Panel();
        readonly Label title = new Label();
        readonly Label subtitle = new Label();
        readonly FlowLayoutPanel toolbar = new FlowLayoutPanel();
        readonly Panel banner = new Panel();
        readonly Label bannerText = new Label();
        readonly FlatButton bBanner = new FlatButton("Settings", Glyphs2.Settings);
        readonly FlowLayoutPanel chips = new FlowLayoutPanel();
        readonly TabStrip tabs = new TabStrip();
        readonly Panel content = new Panel();
        readonly Panel statusBar = new Panel();
        readonly Label statusLeft = new Label();
        readonly Label statusRight = new Label();

        FlatButton bRefresh, bReset, bPxeLog, bTheme, bSettings, bAbout;
        StatChip cMachines, cFlags, cUnknown, cRecords, cDeleted, cRecent;

        readonly Table<Machine> machines = new Table<Machine>();
        readonly RichTextBox machineDetail = Detail();
        readonly Panel detailFooter = new Panel();
        readonly FlatButton bResetSelected = new FlatButton("Reset", Glyphs2.Reset);
        readonly FlatButton bCopyDetail = new FlatButton("Copy details", Glyphs.Copy);
        readonly Table<PxeFlag> flags = new Table<PxeFlag>();
        readonly Table<Activity> activity = new Table<Activity>();
        readonly Label activityTitle = new Label();
        readonly FlatButton bActivityLog = new FlatButton("Open the activity log in LogVice", Glyphs.Log);
        readonly Control[] views = new Control[3];
        readonly List<Control> bars = new List<Control>();
        readonly TextBox search = new TextBox();
        readonly System.Windows.Forms.Timer searchTimer = new System.Windows.Forms.Timer();

        public MainForm(bool demoMode)
        {
            demo = demoMode;
            Safe.Reporter = delegate (Exception ex) { Status("That did not work: " + ex.Message); };
            Safe.Status = delegate (string s) { Status(s); };
            Text = "PxeVice";
            Icon = Program.AppIcon();
            Font = Fonts.Ui;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Dpi.S(1280), Dpi.S(800));
            MinimumSize = new Size(Dpi.S(900), Dpi.S(560));
            LoadBounds();
            BuildHeader();
            BuildViews();
            Controls.Add(content);
            Controls.Add(tabs);
            Controls.Add(chips);
            Controls.Add(banner);
            busyBar.Dock = DockStyle.Top;
            Controls.Add(busyBar);
            themed.Add(busyBar);
            Controls.Add(header);
            Controls.Add(statusBar);
            theme = Theme.Named(Prefs.Get("Theme", "System"));
            ApplyTheme();
            tabs.SelectedChanged += Safe.Wrap(delegate { ShowView(); });
            ShowView();
            ShowMachine(null);
            Shown += Safe.Wrap(delegate { Reload(); });
        }

        // ---------- building ----------

        void BuildHeader()
        {
            header.Dock = DockStyle.Top;
            header.Height = Dpi.S(64);
            title.Text = "PxeVice";
            title.Font = Fonts.Title;
            title.AutoSize = true;
            title.Location = new Point(Dpi.S(20), Dpi.S(8));
            subtitle.AutoSize = true;
            subtitle.Font = Fonts.Small;
            subtitle.Location = new Point(Dpi.S(22), Dpi.S(38));
            subtitle.Text = demo ? "Demo data" : "Connecting…";
            toolbar.Dock = DockStyle.Right;
            toolbar.AutoSize = true;
            toolbar.WrapContents = false;
            toolbar.FlowDirection = FlowDirection.LeftToRight;
            toolbar.Padding = new Padding(0, Dpi.S(6), Dpi.S(12), 0);

            bRefresh = Button("Refresh", Glyphs.Refresh, delegate { Reload(); });
            bReset = Button("Reset", Glyphs2.Reset, delegate { ResetSelected(); });
            bPxeLog = Button("SMSPXE.log", Glyphs.Log, delegate { PxeLogMenu(); });
            bTheme = Button("", Glyphs.Theme, delegate { CycleTheme(); });
            bSettings = Button("", Glyphs2.Settings, delegate { OpenSettings(); });
            bAbout = Button("", Glyphs.Info, delegate { About(); });
            if (Fonts.Icons == null)
            {
                bTheme.Text = "Theme";
                bSettings.Text = "Settings";
                bAbout.Text = "About";
            }
            Tip(bRefresh, "Read the site again (F5)");
            Tip(bReset, "Clear what is left of the selected machines, so they can PXE boot again (Ctrl+R)");
            Tip(bPxeLog, "Open SMSPXE.log from the PXE distribution point in LogVice (Ctrl+L)");
            Tip(bTheme, "Change the theme");
            Tip(bSettings, "Settings (Ctrl+,)");
            Tip(bAbout, "About PxeVice and the command line (F1)");
            foreach (FlatButton b in new FlatButton[] { bRefresh, bReset, bPxeLog, bTheme, bSettings, bAbout })
            {
                b.Margin = new Padding(Dpi.S(2), 0, 0, 0);
                toolbar.Controls.Add(b);
            }
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(toolbar);

            banner.Dock = DockStyle.Top;
            banner.Height = Dpi.S(44);
            banner.Padding = new Padding(Dpi.S(20), Dpi.S(6), Dpi.S(20), Dpi.S(6));
            banner.Visible = false;
            bannerText.Dock = DockStyle.Fill;
            bannerText.TextAlign = ContentAlignment.MiddleLeft;
            bannerText.AutoEllipsis = true;
            bBanner.Primary = true;
            bBanner.Dock = DockStyle.Right;
            bBanner.Click += Safe.Wrap(delegate { OpenSettings(); });
            themed.Add(bBanner);
            banner.Controls.Add(bannerText);
            banner.Controls.Add(bBanner);

            chips.Dock = DockStyle.Top;
            chips.Height = Dpi.S(78);
            chips.Padding = new Padding(Dpi.S(18), Dpi.S(8), Dpi.S(18), Dpi.S(8));
            chips.WrapContents = false;
            cMachines = Chip("Machines", 0, delegate { Filter(null); });
            cRecent = Chip("Last 24 hours", 0, delegate { Filter(FilterRecent); });
            cFlags = Chip("PXE flags", 0, delegate { tabs.Selected = TabFlags; });
            cUnknown = Chip("Unknown computers", 1, delegate { Filter(FilterUnknown); });
            cRecords = Chip("Unknown device records", 2, delegate { Filter(FilterRecords); });
            cDeleted = Chip("Deleted devices", 1, delegate { Filter(FilterDeleted); });
            Tip(cMachines, "Every machine with something left behind");
            Tip(cRecent, "Machines that tried to PXE boot, or got an Unknown record, in the last 24 hours");
            Tip(cFlags, "Rows in LastPXEAdvertisement: deployments offered through PXE");
            Tip(cUnknown, "PXE flags recorded for the Unknown Computer objects, which the console cannot clear per machine");
            Tip(cRecords, "Device records named Unknown, left by a task sequence that failed before the computer got a name");
            Tip(cDeleted, "PXE flags of devices that were deleted from the console");

            tabs.Dock = DockStyle.Top;
            tabs.SetTabs("Machines", "PXE flags", "Activity");
            themed.Add(tabs);

            content.Dock = DockStyle.Fill;
            content.Padding = new Padding(Dpi.S(16), Dpi.S(12), Dpi.S(16), Dpi.S(8));

            statusBar.Dock = DockStyle.Bottom;
            statusBar.Height = Dpi.S(28);
            statusBar.Padding = new Padding(Dpi.S(18), 0, Dpi.S(18), 0);
            statusLeft.Dock = DockStyle.Fill;
            statusLeft.TextAlign = ContentAlignment.MiddleLeft;
            statusLeft.AutoEllipsis = true;
            statusRight.Dock = DockStyle.Right;
            statusRight.AutoSize = true;
            statusRight.TextAlign = ContentAlignment.MiddleRight;
            statusRight.Padding = new Padding(0, Dpi.S(6), 0, 0);
            statusRight.Text = "PxeVice " + Cli.Version;
            statusBar.Controls.Add(statusLeft);
            statusBar.Controls.Add(statusRight);
        }

        void BuildViews()
        {
            // Machines
            Panel filters = new Panel();
            filters.Dock = DockStyle.Top;
            filters.Height = Dpi.S(44);
            FlowLayoutPanel fl = new FlowLayoutPanel();
            fl.Dock = DockStyle.Fill;
            fl.WrapContents = false;
            search.Width = Dpi.S(340);
            search.BorderStyle = BorderStyle.FixedSingle;
            search.Font = Fonts.Ui;
            search.Margin = new Padding(0, Dpi.S(6), Dpi.S(10), 0);
            search.HandleCreated += delegate { Cue(search, "Search name, MAC address, SMBIOS GUID or deployment (Ctrl+F)"); };
            searchTimer.Interval = 200;
            searchTimer.Tick += Safe.Wrap(delegate { searchTimer.Stop(); BindMachines(); });
            search.TextChanged += delegate { searchTimer.Stop(); searchTimer.Start(); };
            fl.Controls.Add(search);
            FlatButton clear = new FlatButton("Clear filters", null);
            clear.Compact = true;
            clear.AutoWidth();
            clear.Margin = new Padding(0, Dpi.S(2), 0, 0);
            clear.Click += Safe.Wrap(delegate { search.Text = ""; Filter(null); });
            themed.Add(clear);
            fl.Controls.Add(clear);
            filters.Controls.Add(fl);
            bars.Add(filters);
            bars.Add(fl);

            machines.Col("Last seen", 140, delegate (Machine m) { return Txt.Local(m.LastSeenUtc); });
            machines.Col("Name", 130, delegate (Machine m) { return m.Name; }).Fill = true;
            machines.Col("Found", 170, delegate (Machine m) { return m.Found; }).Fill = true;
            machines.Col("MAC address", 140, delegate (Machine m) { return m.MacAddress; }).Mono = true;
            machines.Col("SMBIOS GUID", 290, delegate (Machine m) { return m.SmbiosGuid; }).Mono = true;
            machines.Col("Deployment", 90, delegate (Machine m) { return m.Deployments; });
            machines.MultiSelect = true;
            machines.ForeColor = delegate (Machine m)
            {
                if (m.HasUnknownRecord) return theme.ErrFg;
                if (m.Kind == MachineKinds.Deleted) return theme.WarnFg;
                return Color.Empty;
            };
            machines.SelectionChanged += delegate { ShowMachine(machines.SelectedItems); };
            machines.EmptyText = "Nothing is left behind. Every machine can PXE boot.";
            machines.MenuExtras = delegate (ContextMenuStrip m, Machine x)
            {
                Menus.Add(m, "Reset…  (Ctrl+R)", delegate { ResetSelected(); });
                Menus.Separator(m);
                if (x.MacAddress != null) Menus.Add(m, "Copy MAC address", delegate { Menus.Copy(x.MacAddress); });
                if (x.SmbiosGuid != null) Menus.Add(m, "Copy SMBIOS GUID", delegate { Menus.Copy(x.SmbiosGuid); });
                Menus.Add(m, "Copy details", delegate { Menus.Copy(machineDetail.Text.Trim()); });
            };
            machines.Grid.CellDoubleClick += delegate (object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) Safe.Run(ResetSelected); };

            detailFooter.Dock = DockStyle.Bottom;
            detailFooter.Height = Dpi.S(56);
            detailFooter.Padding = new Padding(Dpi.S(12), Dpi.S(12), Dpi.S(12), Dpi.S(12));
            bResetSelected.Primary = true;
            bResetSelected.Dock = DockStyle.Right;
            bResetSelected.Click += Safe.Wrap(delegate { ResetSelected(); });
            bCopyDetail.Dock = DockStyle.Left;
            bCopyDetail.Click += Safe.Wrap(delegate { Menus.Copy(machineDetail.Text.Trim()); });
            themed.Add(bResetSelected);
            themed.Add(bCopyDetail);
            detailFooter.Controls.Add(bResetSelected);
            detailFooter.Controls.Add(bCopyDetail);
            Panel detailHost = new Panel();
            detailHost.Controls.Add(machineDetail);
            detailHost.Controls.Add(detailFooter);
            machineDetail.Dock = DockStyle.Fill;
            views[TabMachines] = Split(machines.Grid, detailHost, filters);

            // PXE flags
            flags.Col("Time", 150, delegate (PxeFlag f) { return Txt.Local(f.TimeUtc); });
            flags.Col("Belongs to", 170, delegate (PxeFlag f) { return f.Owner; }).Fill = true;
            flags.Col("MAC address", 150, delegate (PxeFlag f) { return f.RawMac; }).Mono = true;
            flags.Col("SMBIOS GUID", 300, delegate (PxeFlag f) { return f.SmbiosGuid; }).Mono = true;
            flags.Col("Deployment", 100, delegate (PxeFlag f) { return f.DeploymentId; });
            flags.Col("Task sequence", 200, delegate (PxeFlag f) { return DeploymentName(f.DeploymentId); }).Fill = true;
            flags.Col("Machine ID", 100, delegate (PxeFlag f) { return f.MachineId.ToString(Inv); });
            flags.ForeColor = delegate (PxeFlag f) { return f.IsDeletedDevice ? theme.WarnFg : Color.Empty; };
            flags.EmptyText = "LastPXEAdvertisement is empty.";
            flags.MenuExtras = delegate (ContextMenuStrip m, PxeFlag f)
            {
                Menus.Add(m, "Show the machine", delegate { ShowMachineOf(f); });
                if (f.MacAddress != null) Menus.Add(m, "Copy MAC address", delegate { Menus.Copy(f.MacAddress); });
                if (f.SmbiosGuid != null) Menus.Add(m, "Copy SMBIOS GUID", delegate { Menus.Copy(f.SmbiosGuid); });
            };
            flags.Grid.CellDoubleClick += delegate (object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && flags.Current != null) Safe.Run(delegate { ShowMachineOf(flags.Current); }); };
            views[TabFlags] = Card(flags.Grid);

            // Activity
            Panel ab = new Panel();
            ab.Dock = DockStyle.Top;
            ab.Height = Dpi.S(44);
            ab.Padding = new Padding(0, 0, 0, Dpi.S(8));
            activityTitle.Dock = DockStyle.Fill;
            activityTitle.TextAlign = ContentAlignment.MiddleLeft;
            activityTitle.AutoEllipsis = true;
            activityTitle.Text = "Changes made in this session. Every change is also written to " + ActivityLog.FilePath + ".";
            bActivityLog.Dock = DockStyle.Right;
            bActivityLog.Click += Safe.Wrap(delegate { OpenInLogVice(ActivityLog.FilePath); });
            themed.Add(bActivityLog);
            ab.Controls.Add(activityTitle);
            ab.Controls.Add(bActivityLog);
            bars.Add(ab);
            activity.Col("Time", 150, delegate (Activity a) { return Txt.Local(a.TimeUtc); });
            activity.Col("Machine", 150, delegate (Activity a) { return a.Machine; });
            activity.Col("Change", 420, delegate (Activity a) { return a.Action; }).Fill = true;
            activity.Col("Result", 200, delegate (Activity a) { return (a.Ok ? "" : "Failed: ") + a.Result; }).Fill = true;
            activity.Col("By", 160, delegate (Activity a) { return a.User; });
            activity.ForeColor = delegate (Activity a) { return a.Ok ? Color.Empty : theme.ErrFg; };
            activity.EmptyText = "Nothing has been changed yet. Select a machine and press Reset.";
            Panel ah = new Panel();
            ah.Dock = DockStyle.Fill;
            ah.Controls.Add(Card(activity.Grid));
            ah.Controls.Add(ab);
            views[TabActivity] = ah;

            foreach (Control v in views)
            {
                v.Visible = false;
                content.Controls.Add(v);
            }
            themed.Add(machines);
            themed.Add(flags);
            themed.Add(activity);
        }

        static RichTextBox Detail()
        {
            RichTextBox r = new RichTextBox();
            r.ReadOnly = true;
            r.BorderStyle = BorderStyle.None;
            r.DetectUrls = false;
            r.WordWrap = true;
            r.ScrollBars = RichTextBoxScrollBars.Vertical;
            return r;
        }

        FlatButton Button(string text, string glyph, EventHandler click)
        {
            FlatButton b = new FlatButton(text, glyph);
            b.Click += Safe.Wrap(click);
            themed.Add(b);
            return b;
        }

        StatChip Chip(string label, int tone, EventHandler click)
        {
            StatChip c = new StatChip(label, tone);
            c.Margin = new Padding(0, 0, Dpi.S(10), 0);
            c.Click += Safe.Wrap(click);
            themed.Add(c);
            chips.Controls.Add(c);
            return c;
        }

        readonly ToolTip tips = new ToolTip();

        void Tip(Control c, string text)
        {
            tips.SetToolTip(c, text);
        }

        public static void Cue(TextBox box, string text)
        {
            try { SendMessage(box.Handle, 0x1501, (IntPtr)1, text); } catch (Exception) { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        Control Split(Control grid, Control detail, Control top)
        {
            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            SplitContainer sp = new SplitContainer();
            sp.Dock = DockStyle.Fill;
            sp.Orientation = Orientation.Vertical;
            sp.SplitterWidth = Dpi.S(10);
            sp.FixedPanel = FixedPanel.Panel2;
            sp.Panel1.Controls.Add(Card(grid));
            sp.Panel2.Controls.Add(Card(detail));
            host.Controls.Add(sp);
            if (top != null) host.Controls.Add(top);
            sp.Tag = false;
            host.Resize += delegate
            {
                try
                {
                    int want = Math.Min(Dpi.S(440), Math.Max(Dpi.S(300), host.Width / 3));
                    if (sp.Width > want + Dpi.S(200) && Math.Abs(sp.Width - sp.SplitterDistance - sp.SplitterWidth - want) > Dpi.S(4) && !sp.Tag.Equals(true))
                        sp.SplitterDistance = sp.Width - want - sp.SplitterWidth;
                }
                catch (Exception) { }
            };
            sp.SplitterMoved += delegate { if (sp.Focused) sp.Tag = true; };
            return host;
        }

        static Panel Card(Control inner)
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;
            p.Padding = new Padding(1);
            p.Tag = "card";
            inner.Dock = DockStyle.Fill;
            p.Controls.Add(inner);
            return p;
        }

        // ---------- theme ----------

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void CycleTheme()
        {
            string[] order = { "System", "Light", "Dark", "Vice" };
            string cur = Prefs.Get("Theme", "System");
            int i = Array.IndexOf(order, cur);
            string next = order[(i + 1) % order.Length];
            Prefs.Set("Theme", next);
            theme = Theme.Named(next);
            ApplyTheme();
            Status("Theme: " + (next == "System" ? "follow Windows (" + theme.Name + ")" : next));
        }

        void ApplyTheme()
        {
            Theme t = theme;
            BackColor = t.Bg;
            ForeColor = t.Text;
            foreach (Control c in new Control[] { header, toolbar, chips, content, statusBar })
            {
                c.BackColor = t.Bg;
                c.ForeColor = t.Text;
            }
            title.ForeColor = t.Text;
            subtitle.ForeColor = t.Text3;
            bannerText.ForeColor = t.Text;
            statusLeft.ForeColor = t.Text3;
            statusRight.ForeColor = t.Text3;
            activityTitle.ForeColor = t.Text2;
            foreach (Control c in bars) c.BackColor = t.Bg;
            foreach (Control v in views) Recolor(v, t);
            search.BackColor = t.Field;
            search.ForeColor = t.Text;
            machineDetail.BackColor = t.Card;
            machineDetail.ForeColor = t.Text;
            detailFooter.BackColor = t.Card;
            if (detailFooter.Parent != null) detailFooter.Parent.BackColor = t.Card;
            foreach (IThemed x in themed) x.ApplyTheme(t);
            bResetSelected.BackColor = bCopyDetail.BackColor = t.Card;
            BannerColors();
            try
            {
                int dark = t.IsDark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            }
            catch (Exception) { }
            ShowMachine(machines.SelectedItems);
            Invalidate(true);
        }

        void Recolor(Control c, Theme t)
        {
            if (c is SplitContainer)
            {
                c.BackColor = t.Bg;
                ((SplitContainer)c).Panel1.BackColor = t.Bg;
                ((SplitContainer)c).Panel2.BackColor = t.Bg;
            }
            else if (c is Panel && "card".Equals(c.Tag)) c.BackColor = t.Divider;
            else if (c is Panel || c is FlowLayoutPanel) c.BackColor = t.Bg;
            foreach (Control k in c.Controls) Recolor(k, t);
        }

        bool bannerError;

        void Banner(string text, bool error, string button)
        {
            bannerError = error;
            bannerText.Text = text ?? "";
            banner.Visible = !string.IsNullOrEmpty(text);
            bBanner.Visible = button != null;
            if (button != null) bBanner.Text = button;
            BannerColors();
        }

        void BannerColors()
        {
            banner.BackColor = bannerError ? theme.ErrBg : theme.WarnBg;
            bannerText.ForeColor = theme.Text;
        }

        // ---------- reading ----------

        void Reload()
        {
            if (busy)
            {
                pending = true;
                return;
            }
            busy = true;
            bRefresh.Enabled = false;
            busyBar.Running = true;
            Cursor = Cursors.AppStarting;
            Status(site == null ? "Connecting to the site…" : "Reading PXE flags and Unknown device records…");
            ISite current = site;
            ThreadPool.QueueUserWorkItem(delegate
            {
                ISite s = current;
                SiteData d = null;
                Exception err = null;
                bool connectFailed = false;
                try
                {
                    if (s == null)
                    {
                        try { s = Cli.Connect(demo, SiteOptions.FromPrefs()); }
                        catch (Exception) { connectFailed = true; throw; }
                    }
                    d = Engine.Read(s);
                }
                catch (Exception ex) { err = ex; }
                try { BeginInvoke((MethodInvoker)delegate { Loaded(s, d, err, connectFailed); }); }
                catch (Exception) { }
            });
        }

        void Loaded(ISite s, SiteData d, Exception err, bool connectFailed)
        {
            busy = false;
            bRefresh.Enabled = true;
            busyBar.Running = false;
            Cursor = Cursors.Default;
            try
            {
                site = connectFailed ? null : s;
                if (err != null)
                {
                    string where = connectFailed ? "Could not connect to " + (Prefs.Get("SiteServer", null) ?? Environment.MachineName) : "Could not read " + (s != null ? s.Info.Database + " on " + s.Info.SqlServer : "the site");
                    Banner(where + ": " + err.Message, true, "Settings");
                    Status("Reading failed: " + err.Message);
                }
                else
                {
                    Banner(null, false, null);
                    Bind(d);
                }
            }
            catch (Exception ex) { Safe.Report(ex); }
            if (pending)
            {
                pending = false;
                Reload();
            }
        }

        void Bind(SiteData d)
        {
            data = d;
            SiteInfo info = site.Info;
            subtitle.Text = info.Describe() + " · read " + d.ReadUtc.ToLocalTime().ToString("HH:mm:ss", Inv);
            Text = "PxeVice - " + info.SiteServer + " (" + info.SiteCode + ")" + (demo ? " (demo)" : "");
            DateTime now = DateTime.UtcNow;
            int unknown = 0, deleted = 0, recent = 0, records = 0;
            foreach (Machine m in d.Machines)
            {
                if (m.HasUnknownFlag) unknown++;
                if (m.HasDeletedFlag) deleted++;
                if (m.HasUnknownRecord) records++;
                if (m.LastSeenUtc.HasValue && now - m.LastSeenUtc.Value < TimeSpan.FromHours(24)) recent++;
            }
            cMachines.Count = d.Machines.Count;
            cFlags.Count = d.Flags.Count;
            cUnknown.Count = unknown;
            cRecords.Count = records;
            cDeleted.Count = deleted;
            cRecent.Count = recent;
            foreach (Control c in chips.Controls) c.Invalidate();
            BindMachines();
            flags.SetRows(new List<PxeFlag>(d.Flags));
            tabs.SetText(TabFlags, "PXE flags (" + d.Flags.Count + ")");
            if (pendingDoneMessage != null)
            {
                Status(pendingDoneMessage);
                pendingDoneMessage = null;
            }
            else if (info.Notes.Count > 0) Status(string.Join(" ", info.Notes.ToArray()));
            else Status(d.Machines.Count == 0 ? "Nothing is left behind." : d.Machines.Count + (d.Machines.Count == 1 ? " machine has" : " machines have") + " something left behind. Select one to see why it does not PXE boot.");
        }

        void Filter(string f)
        {
            chipFilter = f;
            cMachines.Selected = f == null && data != null;
            cUnknown.Selected = f == FilterUnknown;
            cRecords.Selected = f == FilterRecords;
            cDeleted.Selected = f == FilterDeleted;
            cRecent.Selected = f == FilterRecent;
            foreach (Control c in chips.Controls) c.Invalidate();
            tabs.Selected = TabMachines;
            BindMachines();
        }

        void BindMachines()
        {
            if (data == null) return;
            string q = search.Text.Trim();
            DateTime now = DateTime.UtcNow;
            List<Machine> list = new List<Machine>();
            foreach (Machine m in data.Machines)
            {
                if (chipFilter == FilterUnknown && !m.HasUnknownFlag) continue;
                if (chipFilter == FilterRecords && !m.HasUnknownRecord) continue;
                if (chipFilter == FilterDeleted && !m.HasDeletedFlag) continue;
                if (chipFilter == FilterRecent && !(m.LastSeenUtc.HasValue && now - m.LastSeenUtc.Value < TimeSpan.FromHours(24))) continue;
                if (q.Length > 0 && !Matches(m, q)) continue;
                list.Add(m);
            }
            machines.SetRows(list);
            tabs.SetText(TabMachines, list.Count == data.Machines.Count ? "Machines (" + list.Count + ")" : "Machines (" + list.Count + " of " + data.Machines.Count + ")");
            ShowMachine(machines.SelectedItems);
        }

        bool Matches(Machine m, string q)
        {
            // A MAC address can be typed with dashes, colons or nothing in between.
            string mac = Ids.Mac(q);
            foreach (string x in m.AllMacs) if (mac != null ? x == mac : Has(x, q)) return true;
            if (Has(m.Name, q) || Has(m.SmbiosGuid, q) || Has(m.Deployments, q) || Has(m.Found, q) || Has(m.ResourceIds, q)) return true;
            foreach (PxeFlag f in m.Flags) if (Has(DeploymentName(f.DeploymentId), q)) return true;
            return false;
        }

        static bool Has(string s, string q)
        {
            return s != null && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        string DeploymentName(string id)
        {
            string name;
            if (data != null && id != null && data.DeploymentNames.TryGetValue(id, out name)) return name;
            return "";
        }

        void ShowView()
        {
            for (int i = 0; i < views.Length; i++) views[i].Visible = i == tabs.Selected;
            views[tabs.Selected].BringToFront();
        }

        void ShowMachineOf(PxeFlag f)
        {
            if (data == null) return;
            search.Text = "";
            Filter(null);
            foreach (Machine m in machines.Rows)
            {
                if (m.Flags.Contains(f))
                {
                    machines.Select(m);
                    machines.Grid.Focus();
                    return;
                }
            }
        }

        void Status(string text)
        {
            statusLeft.Text = text;
        }

        // ---------- detail ----------

        void Heading(RichTextBox r, string text)
        {
            r.SelectionFont = Fonts.Title;
            r.SelectionColor = theme.Text;
            r.AppendText(text + "\n");
        }

        void Section(RichTextBox r, string text)
        {
            r.SelectionFont = Fonts.UiSemibold;
            r.SelectionColor = theme.Text;
            r.AppendText("\n" + text + "\n");
        }

        void Field(RichTextBox r, string label, string value, bool mono)
        {
            if (string.IsNullOrEmpty(value)) return;
            r.SelectionFont = Fonts.Small;
            r.SelectionColor = theme.Text3;
            r.AppendText(label + "\n");
            r.SelectionFont = mono ? Fonts.Mono : Fonts.Ui;
            r.SelectionColor = theme.Text;
            r.AppendText(value + "\n");
        }

        void Line(RichTextBox r, string tag, Color tagColor, string text)
        {
            r.SelectionFont = Fonts.UiSemibold;
            r.SelectionColor = tagColor;
            r.AppendText(tag + "  ");
            r.SelectionFont = Fonts.Ui;
            r.SelectionColor = theme.Text;
            r.AppendText(text + "\n");
        }

        void Plain(RichTextBox r, string text, Color c)
        {
            r.SelectionFont = Fonts.Ui;
            r.SelectionColor = c;
            r.AppendText(text + "\n");
        }

        void ShowMachine(List<Machine> sel)
        {
            RichTextBox r = machineDetail;
            r.SuspendLayout();
            r.Clear();
            r.SelectionIndent = Dpi.S(12);
            r.SelectionRightIndent = Dpi.S(8);
            int n = sel == null ? 0 : sel.Count;
            bResetSelected.Enabled = n > 0 && !busy;
            bReset.Enabled = n > 0 && !busy;
            bCopyDetail.Enabled = n > 0;
            bResetSelected.Text = n > 1 ? "Reset " + n + " machines" : "Reset";
            bResetSelected.AutoWidth();
            if (n == 0)
            {
                r.AppendText("\n");
                Plain(r, data == null ? "Reading the site…" : data.Machines.Count == 0 ? "Nothing is left behind, so every machine can PXE boot." : "Select a machine to see why it does not PXE boot.", theme.Text3);
            }
            else if (n > 1)
            {
                r.AppendText("\n");
                Heading(r, n + " machines");
                foreach (Machine m in sel) Line(r, m.Name, theme.Text, m.Found + (m.MacAddress != null ? "  ·  " + m.MacAddress : ""));
                Section(r, "Reset");
                Plain(r, "PxeVice shows every change for these machines before it makes them.", theme.Text2);
            }
            else Describe(r, sel[0]);
            r.SelectionStart = 0;
            r.ScrollToCaret();
            r.ResumeLayout();
        }

        void Describe(RichTextBox r, Machine m)
        {
            DateTime now = DateTime.UtcNow;
            r.AppendText("\n");
            Heading(r, m.Name);
            Plain(r, m.Kind + (m.LastSeenUtc.HasValue ? "  ·  last seen " + Txt.Ago(m.LastSeenUtc, now) : ""), theme.Text3);

            Section(r, "Why it does not PXE boot");
            foreach (PxeFlag f in m.Flags)
            {
                string when = f.TimeUtc.HasValue ? " on " + Txt.Local(f.TimeUtc) + " (" + Txt.Ago(f.TimeUtc, now) + ")" : "";
                string what = "Deployment " + m.DeploymentText(f.DeploymentId) + " was offered through PXE" + when + ". A required deployment is not offered again while this flag is there.";
                if (f.IsUnknownComputer) what += " It is recorded for the Unknown Computer object, so the console cannot clear it for this machine alone.";
                else if (f.IsDeletedDevice) what += " The device it belongs to was deleted, so the console cannot show it.";
                else what += " Clear Required PXE Deployments on the device clears it.";
                Line(r, "PXE flag", theme.WarnFg, what);
            }
            foreach (DeviceRecord d in m.Devices)
            {
                if (!d.IsUnknownRecord) continue;
                Line(r, "Unknown record", theme.ErrFg, "Device " + d.ResourceId.ToString(Inv) + " named Unknown has this machine's MAC address or SMBIOS GUID" +
                    (d.CreatedUtc.HasValue ? ", created " + Txt.Ago(d.CreatedUtc, now) : "") + ". A task sequence created it and failed before the computer got a name. While it exists the machine is known, so it does not get deployments for Unknown Computers.");
            }
            if (m.Flags.Count == 0 && !m.HasUnknownRecord) Plain(r, "Nothing that blocks PXE was found.", theme.Text2);

            Section(r, "Machine");
            Field(r, "MAC addresses", string.Join("\n", m.AllMacs.ToArray()), true);
            Field(r, "SMBIOS GUID", m.SmbiosGuid, true);
            List<string> deps = new List<string>();
            foreach (PxeFlag f in m.Flags) if (!string.IsNullOrEmpty(f.DeploymentId) && !deps.Contains(m.DeploymentText(f.DeploymentId))) deps.Add(m.DeploymentText(f.DeploymentId));
            Field(r, "Deployments", string.Join("\n", deps.ToArray()), false);
            Field(r, "Last PXE boot", Txt.Local(m.LastPxeUtc), false);
            List<string> devs = new List<string>();
            foreach (DeviceRecord d in m.Devices) devs.Add(d.Name + " (" + d.ResourceId.ToString(Inv) + ")" + (d.CreatedUtc.HasValue ? ", created " + Txt.Local(d.CreatedUtc) : ""));
            foreach (PxeFlag f in m.Flags) if (f.DeviceName != null) devs.Add(f.DeviceName + " (" + f.MachineId.ToString(Inv) + ")");
            Field(r, "Device records", string.Join("\n", devs.ToArray()), false);
            List<string> ids = new List<string>();
            foreach (PxeFlag f in m.Flags) ids.Add(f.MachineId.ToString(Inv) + (f.IsUnknownComputer ? " (Unknown Computer)" : f.IsDeletedDevice ? " (deleted)" : ""));
            Field(r, "Machine ID in LastPXEAdvertisement", string.Join("\n", ids.ToArray()), true);

            Section(r, "Reset will");
            bool deleteUnknown = Prefs.GetBool("DeleteUnknown", true);
            int steps = 0;
            if (m.HasNamedDevice || m.HasDeviceFlag || m.Devices.Count > 0) { Plain(r, "· Clear the PXE flag with Clear Required PXE Deployments", theme.Text); steps++; }
            if (m.HasUnknownRecord && deleteUnknown) { Plain(r, "· Delete the Unknown device record", theme.Text); steps++; }
            if (m.Flags.Count > 0) { Plain(r, "· Delete this machine's rows in LastPXEAdvertisement", theme.Text); steps++; }
            if (steps == 0) Plain(r, "Nothing.", theme.Text2);
            Plain(r, "PxeVice reads the site again and shows every change before it makes them.", theme.Text3);
        }

        // ---------- reset ----------

        void ResetSelected()
        {
            if (busy || site == null || data == null) return;
            List<Machine> sel = machines.SelectedItems;
            if (sel.Count == 0)
            {
                Status("Select one or more machines first.");
                return;
            }
            busy = true;
            busyBar.Running = true;
            Cursor = Cursors.AppStarting;
            Status("Checking what is left for " + (sel.Count == 1 ? sel[0].Name : sel.Count + " machines") + "…");
            ISite s = site;
            ResetOptions everything = new ResetOptions();
            everything.DeleteUnknownRecords = true;
            everything.DeleteDevices = true;
            everything.RestartPxeService = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Plan p = null;
                Exception err = null;
                try { p = Engine.BuildPlan(s, sel, everything); }
                catch (Exception ex) { err = ex; }
                try { BeginInvoke((MethodInvoker)delegate { Planned(s, sel, p, err); }); }
                catch (Exception) { }
            });
        }

        void Planned(ISite s, List<Machine> sel, Plan full, Exception err)
        {
            busy = false;
            busyBar.Running = false;
            Cursor = Cursors.Default;
            if (err != null)
            {
                Status("Could not check the machines: " + err.Message);
                return;
            }
            Status("Check the changes, then press Reset.");
            Plan chosen;
            using (ResetDialog d = new ResetDialog(theme, sel, full, s.Info))
            {
                if (d.ShowDialog(this) != DialogResult.OK)
                {
                    Status("Reset cancelled. Nothing was changed.");
                    return;
                }
                chosen = d.Chosen;
            }
            Run(s, chosen);
        }

        void Run(ISite s, Plan plan)
        {
            busy = true;
            busyBar.Running = true;
            bRefresh.Enabled = false;
            ShowMachine(machines.SelectedItems);
            Cursor = Cursors.AppStarting;
            Status("Resetting…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Activity> done = null;
                Exception err = null;
                try
                {
                    done = Engine.Execute(s, plan, delegate (Activity a)
                    {
                        try { BeginInvoke((MethodInvoker)delegate { Status(a.Action + ": " + a.Result); }); } catch (Exception) { }
                    });
                }
                catch (Exception ex) { err = ex; }
                try { BeginInvoke((MethodInvoker)delegate { Ran(done, err); }); }
                catch (Exception) { }
            });
        }

        void Ran(List<Activity> done, Exception err)
        {
            busy = false;
            busyBar.Running = false;
            bRefresh.Enabled = true;
            Cursor = Cursors.Default;
            if (done != null)
            {
                activities.InsertRange(0, done);
                activity.SetRows(new List<Activity>(activities));
            }
            int failed = 0;
            if (done != null) foreach (Activity a in done) if (!a.Ok) failed++;
            tabs.SetText(TabActivity, "Activity (" + activities.Count + ")");
            Reload();
            if (err != null) Status("The reset stopped: " + err.Message);
            else if (failed > 0)
            {
                Status(failed + (failed == 1 ? " change" : " changes") + " failed. See the Activity tab.");
                tabs.Selected = TabActivity;
            }
            else
            {
                pendingDoneMessage = done == null || done.Count == 0 ? "Nothing was changed." : "Done: " + done.Count + (done.Count == 1 ? " change" : " changes") + ". PXE boot the machine again.";
                Status(pendingDoneMessage);
            }
        }

        string pendingDoneMessage;

        // ---------- LogVice ----------

        void PxeLogMenu()
        {
            if (site == null)
            {
                Status("Connect to the site first.");
                return;
            }
            List<string> servers = site.Info.PxeServers;
            if (servers.Count == 0)
            {
                Status("No PXE distribution point is known. Add one in Settings.");
                return;
            }
            if (servers.Count == 1)
            {
                OpenPxeLog(servers[0]);
                return;
            }
            ContextMenuStrip m = Menus.New(theme);
            foreach (string s in servers)
            {
                string server = s;
                Menus.Add(m, server, delegate { OpenPxeLog(server); });
            }
            m.Show(bPxeLog, new Point(0, bPxeLog.Height));
        }

        void OpenPxeLog(string server)
        {
            ISite s = site;
            Status("Looking for SMSPXE.log on " + server + "…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string path = null;
                try { path = s.PxeLog(server); }
                catch (Exception) { }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (path == null) Status("SMSPXE.log was not found on " + server + ". PxeVice looks in SMS_DP$\\sms\\logs and SMS_" + s.Info.SiteCode + "\\Logs.");
                        else OpenInLogVice(path);
                    });
                }
                catch (Exception) { }
            });
        }

        void OpenInLogVice(string path)
        {
            string exe = LogViceLink.Find();
            if (exe == null)
            {
                DialogResult r = MessageBox.Show(this, "LogVice was not found on this computer. Do you want to locate LogVice.exe?\n\nChoose No to open the log with the program Windows uses for .log files.", "PxeVice", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.No)
                {
                    OpenPath(path);
                    return;
                }
                exe = LocateLogVice();
                if (exe == null) return;
            }
            try
            {
                LogViceLink.Open(exe, path);
                Status("Opened in LogVice: " + path);
            }
            catch (Exception ex) { Status("LogVice did not start: " + ex.Message); }
        }

        string LocateLogVice()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "LogVice|LogVice.exe|Programs|*.exe";
                d.Title = "Locate LogVice";
                if (d.ShowDialog(this) != DialogResult.OK) return null;
                Prefs.Set("LogVicePath", d.FileName);
                return d.FileName;
            }
        }

        public static void OpenPath(string path)
        {
            try
            {
                if (Directory.Exists(path) || File.Exists(path))
                {
                    if (Program.IsAdmin) System.Diagnostics.Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + path + "\"");
                    else System.Diagnostics.Process.Start(path);
                }
            }
            catch (Exception) { }
        }

        // ---------- dialogs ----------

        void OpenSettings()
        {
            using (SettingsDialog d = new SettingsDialog(theme, site != null ? site.Info : null))
            {
                d.ThemeChanged += delegate
                {
                    theme = Theme.Named(Prefs.Get("Theme", "System"));
                    ApplyTheme();
                };
                d.ShowDialog(this);
                ShowMachine(machines.SelectedItems);
                if (d.SiteChanged && !demo)
                {
                    if (site != null) site.Dispose();
                    site = null;
                    data = null;
                    machines.SetRows(new List<Machine>());
                    flags.SetRows(new List<PxeFlag>());
                    subtitle.Text = "Connecting…";
                    Reload();
                }
            }
        }

        void About()
        {
            using (AboutDialog d = new AboutDialog(theme)) d.ShowDialog(this);
        }

        // ---------- window ----------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            try
            {
                switch (keyData)
                {
                    case Keys.F5: Reload(); return true;
                    case Keys.F1: About(); return true;
                    case Keys.Control | Keys.R: ResetSelected(); return true;
                    case Keys.Control | Keys.L: PxeLogMenu(); return true;
                    case Keys.Control | Keys.Oemcomma: OpenSettings(); return true;
                    case Keys.Control | Keys.F: tabs.Selected = TabMachines; search.Focus(); search.SelectAll(); return true;
                    case Keys.Escape:
                        if (search.Focused && search.Text.Length > 0) { search.Text = ""; return true; }
                        break;
                }
            }
            catch (Exception ex)
            {
                Safe.Report(ex);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void LoadBounds()
        {
            string b = Prefs.Get("Bounds", null);
            if (b == null) return;
            string[] p = b.Split(',');
            int x, y, w, h;
            if (p.Length != 4 || !int.TryParse(p[0], out x) || !int.TryParse(p[1], out y) || !int.TryParse(p[2], out w) || !int.TryParse(p[3], out h)) return;
            Rectangle r = new Rectangle(x, y, w, h);
            foreach (Screen s in Screen.AllScreens)
            {
                if (!s.WorkingArea.IntersectsWith(r)) continue;
                StartPosition = FormStartPosition.Manual;
                Bounds = r;
                if (Prefs.Get("Maximized", "0") == "1") WindowState = FormWindowState.Maximized;
                return;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            Prefs.Set("Bounds", r.X + "," + r.Y + "," + r.Width + "," + r.Height);
            Prefs.Set("Maximized", WindowState == FormWindowState.Maximized ? "1" : "0");
            try { if (site != null) site.Dispose(); } catch (Exception) { }
            base.OnFormClosing(e);
        }
    }

    sealed class MenuColors : ProfessionalColorTable
    {
        readonly Theme t;

        public MenuColors(Theme theme)
        {
            t = theme;
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground { get { return t.Card; } }
        public override Color MenuBorder { get { return t.Divider; } }
        public override Color MenuItemBorder { get { return t.Hover; } }
        public override Color MenuItemSelected { get { return t.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return t.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return t.Hover; } }
        public override Color ImageMarginGradientBegin { get { return t.Card; } }
        public override Color ImageMarginGradientMiddle { get { return t.Card; } }
        public override Color ImageMarginGradientEnd { get { return t.Card; } }
        public override Color SeparatorDark { get { return t.Divider; } }
        public override Color SeparatorLight { get { return t.Divider; } }
    }
}
