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
        const int TabMachines = 0, TabRequests = 1, TabFlags = 2, TabActivity = 3;
        const string FilterNotServiced = "notserviced", FilterUnknown = "unknown", FilterRecords = "records", FilterDeleted = "deleted";

        Theme theme;
        readonly bool demo;
        bool busy, pending, quietPending;
        ISite site;
        SiteData data;
        PxeLogStore store = new PxeLogStore();
        CheckCache cache = new CheckCache();
        DateTime cacheUtc = DateTime.MinValue;
        readonly Dictionary<string, bool> checking = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> seen = new Dictionary<string, bool>();
        bool firstRead = true;
        readonly List<Activity> activities = new List<Activity>();
        string chipFilter;

        // Machines that were reset and whose next PXE boot PxeVice waits for.
        sealed class Awaited
        {
            public string Key, Name, Mac, Guid;
            public DateTime Since;
            // Resource IDs of device records this reset deleted, and whether the PXE service was restarted with it.
            public List<long> Deleted = new List<long>();
            public bool Restarted;
        }
        readonly List<Awaited> awaiting = new List<Awaited>();

        bool watching;
        readonly System.Windows.Forms.Timer watchTimer = new System.Windows.Forms.Timer();

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

        FlatButton bRefresh, bWatch, bReset, bExport, bPxeLog, bSite, bTheme, bSettings, bAbout;
        StatChip cMachines, cNotServiced, cRequests, cFlags, cUnknown, cRecords, cDeleted;

        readonly Table<Machine> machines = new Table<Machine>();
        readonly RichTextBox machineDetail = Detail();
        readonly Panel detailFooter = new Panel();
        readonly FlatButton bResetSelected = new FlatButton("Reset", Glyphs2.Reset);
        readonly FlatButton bCopyDetail = new FlatButton("Copy for the ticket", Glyphs.Copy);
        readonly Table<PxeRequest> requests = new Table<PxeRequest>();
        readonly RichTextBox requestDetail = Detail();
        readonly Panel requestFooter = new Panel();
        readonly FlatButton bRequestMachine = new FlatButton("Show the machine", Glyphs2.Device);
        readonly FlatButton bRequestLog = new FlatButton("SMSPXE.log", Glyphs.Log);
        readonly Table<PxeFlag> flags = new Table<PxeFlag>();
        readonly Table<Activity> activity = new Table<Activity>();
        readonly Label activityTitle = new Label();
        readonly FlatButton bActivityLog = new FlatButton("Open the activity log in LogVice", Glyphs.Log);
        readonly Control[] views = new Control[4];
        readonly List<Control> bars = new List<Control>();
        readonly TextBox search = new TextBox();
        readonly System.Windows.Forms.Timer searchTimer = new System.Windows.Forms.Timer();

        public MainForm(bool demoMode)
        {
            demo = demoMode;
            Safe.Reporter = delegate (Exception ex) { Status("That did not work: " + ex.Message); };
            Safe.Status = delegate (string s) { Status(s); };
            Engine.RequestWindow = TimeSpan.FromHours(Math.Max(1, Prefs.GetInt("RequestHours", 24)));
            Text = "PxeVice";
            Icon = Program.AppIcon();
            Font = Fonts.Ui;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Dpi.S(1320), Dpi.S(820));
            MinimumSize = new Size(Dpi.S(960), Dpi.S(560));
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
            theme = Theme.Named(Program.ThemeOverride ?? Theme.SavedName());
            ApplyTheme();
            tabs.SelectedChanged += Safe.Wrap(delegate { ShowView(); });
            ShowView();
            ShowMachine(null);
            ShowRequest(null);
            watchTimer.Tick += Safe.Wrap(delegate { if (!busy) Reload(true); });
            Shown += Safe.Wrap(delegate
            {
                Reload(false);
                if (Prefs.GetBool("WatchOnStart", false)) SetWatch(true);
                if (!demo) StartUpdateCheck();
            });
        }

        // ---------- updates ----------

        // Once a day at the most: asks GitHub for the latest release and says so when there is a newer one.
        void StartUpdateCheck()
        {
            Updater.CleanUp(Program.Location);
            if (Updater.CheckBlocked != null || !Updater.DueAt(DateTime.UtcNow)) return;
            Updater.CheckAsync(delegate (UpdateInfo r)
            {
                try { BeginInvoke((MethodInvoker)delegate { UpdateChecked(r); }); }
                catch (Exception) { }
            });
        }

        void UpdateChecked(UpdateInfo r)
        {
            if (r == null || !r.Newer || r.Error != null) return;
            // Do not take the place of a message about a reset that is waiting.
            if (banner.Visible) { Status("PxeVice " + r.Latest + " is available. Settings > Updates shows what is new."); return; }
            Banner("PxeVice " + r.Latest + " is available. You have " + Cli.Version + ".", false, "Update");
        }

        void ShowUpdate()
        {
            UpdateInfo r = Updater.Last;
            if (r == null || !r.Newer) return;
            using (UpdateDialog d = new UpdateDialog(theme, r)) d.ShowDialog(this);
            if (bannerAction == "Update" && !Updater.CanInstall(r, Updater.IsInstalled(Program.Location))) Banner(null, false, null);
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

            bRefresh = Button("Refresh", Glyphs.Refresh, delegate { Reload(false); });
            bWatch = Button("Watch", Glyphs.Watch, delegate { SetWatch(!watching); });
            bReset = Button("Reset", Glyphs2.Reset, delegate { ResetSelected(); });
            bExport = Button("Export", Glyphs.Export, delegate { ExportMenu(); });
            bPxeLog = Button("SMSPXE.log", Glyphs.Log, delegate { PxeLogMenu(null); });
            bSite = Button("Site", Glyphs3.Server, delegate { SiteMenu(); });
            bTheme = Button("", Glyphs.Theme, delegate { CycleTheme(); });
            bSettings = Button("", Glyphs2.Settings, delegate { OpenSettings(); });
            bAbout = Button("", Glyphs.Info, delegate { About(); });
            if (Fonts.Icons == null)
            {
                bTheme.Text = "Theme";
                bSettings.Text = "Settings";
                bAbout.Text = "About";
            }
            Tip(bRefresh, "Read the site and SMSPXE.log again (F5)");
            Tip(bWatch, "Read again every few seconds, and show new PXE boots as they come (Ctrl+W)");
            Tip(bReset, "Clear what is left of the selected machines, so they can PXE boot again (Ctrl+R)");
            Tip(bExport, "Save a report for the ticket, copy the check, or save the PXE requests (Ctrl+E)");
            Tip(bPxeLog, "Open SMSPXE.log from the PXE distribution point in LogVice (Ctrl+L)");
            Tip(bSite, "Switch between saved sites");
            Tip(bTheme, "Change the theme");
            Tip(bSettings, "Settings (Ctrl+,)");
            Tip(bAbout, "About PxeVice and the command line (F1)");
            foreach (FlatButton b in new FlatButton[] { bRefresh, bWatch, bReset, bExport, bPxeLog, bSite, bTheme, bSettings, bAbout })
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
            bBanner.Click += Safe.Wrap(delegate { BannerClick(); });
            themed.Add(bBanner);
            banner.Controls.Add(bannerText);
            banner.Controls.Add(bBanner);

            chips.Dock = DockStyle.Top;
            chips.Height = Dpi.S(78);
            chips.Padding = new Padding(Dpi.S(18), Dpi.S(8), Dpi.S(18), Dpi.S(8));
            chips.WrapContents = false;
            cMachines = Chip("Machines", 0, delegate { Filter(null); });
            cNotServiced = Chip("Not serviced", 2, delegate { Filter(FilterNotServiced); });
            cRequests = Chip("PXE boots", 0, delegate { tabs.Selected = TabRequests; });
            cFlags = Chip("PXE flags", 0, delegate { tabs.Selected = TabFlags; });
            cUnknown = Chip("Unknown computers", 1, delegate { Filter(FilterUnknown); });
            cRecords = Chip("Unknown device records", 2, delegate { Filter(FilterRecords); });
            cDeleted = Chip("Deleted devices", 1, delegate { Filter(FilterDeleted); });
            Tip(cMachines, "Every machine with something left behind, or a PXE boot that was not serviced");
            Tip(cNotServiced, "Machines whose last PXE boot found no deployment");
            Tip(cRequests, "PXE boots in SMSPXE.log in the time set in Settings");
            Tip(cFlags, "Rows in LastPXEAdvertisement: deployments offered through PXE");
            Tip(cUnknown, "PXE flags recorded for the Unknown Computer objects, which the console cannot clear per machine");
            Tip(cRecords, "Device records named Unknown, left by a task sequence that failed before the computer got a name");
            Tip(cDeleted, "PXE flags of devices that were deleted from the console");

            tabs.Dock = DockStyle.Top;
            tabs.SetTabs("Machines", "PXE boots", "PXE flags", "Activity");
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
            search.Width = Dpi.S(360);
            search.BorderStyle = BorderStyle.FixedSingle;
            search.Font = Fonts.Ui;
            search.Margin = new Padding(0, Dpi.S(6), Dpi.S(10), 0);
            search.HandleCreated += delegate { Cue(search, "Search name, MAC address, SMBIOS GUID or deployment (Ctrl+F)"); };
            searchTimer.Interval = 200;
            searchTimer.Tick += Safe.Wrap(delegate { searchTimer.Stop(); BindMachines(); BindRequests(); });
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
            machines.Col("Found", 190, delegate (Machine m) { return m.Found; }).Fill = true;
            machines.Col("MAC address", 140, delegate (Machine m) { return m.MacAddress; }).Mono = true;
            machines.Col("SMBIOS GUID", 290, delegate (Machine m) { return m.SmbiosGuid; }).Mono = true;
            machines.Col("Deployment", 90, delegate (Machine m) { return m.Deployments; });
            machines.MultiSelect = true;
            machines.ForeColor = delegate (Machine m)
            {
                if (m.NotServiced || m.HasUnknownRecord) return theme.ErrFg;
                if (m.Kind == MachineKinds.Deleted) return theme.WarnFg;
                return Color.Empty;
            };
            machines.SelectionChanged += delegate { ShowMachine(machines.SelectedItems); };
            machines.EmptyText = "Nothing is left behind, and no PXE boot went unserviced.";
            machines.MenuExtras = delegate (ContextMenuStrip m, Machine x)
            {
                Menus.Add(m, "Reset…  (Ctrl+R)", delegate { ResetSelected(); });
                Menus.Add(m, "Report for the ticket…  (Ctrl+E)", delegate { ExportReport(); });
                Menus.Separator(m);
                if (x.MacAddress != null) Menus.Add(m, "Copy MAC address", delegate { Menus.Copy(x.MacAddress); });
                if (x.SmbiosGuid != null) Menus.Add(m, "Copy SMBIOS GUID", delegate { Menus.Copy(x.SmbiosGuid); });
                Menus.Add(m, "Copy the check for the ticket", delegate { CopyCheck(); });
                if (x.Requests.Count > 0) Menus.Add(m, "Show its PXE boots", delegate { ShowRequestsOf(x); });
            };
            machines.Grid.CellDoubleClick += delegate (object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) Safe.Run(ResetSelected); };

            detailFooter.Dock = DockStyle.Bottom;
            detailFooter.Height = Dpi.S(56);
            detailFooter.Padding = new Padding(Dpi.S(12), Dpi.S(12), Dpi.S(12), Dpi.S(12));
            bResetSelected.Primary = true;
            bResetSelected.Dock = DockStyle.Right;
            bResetSelected.Click += Safe.Wrap(delegate { ResetSelected(); });
            bCopyDetail.Dock = DockStyle.Left;
            bCopyDetail.Click += Safe.Wrap(delegate { CopyCheck(); });
            themed.Add(bResetSelected);
            themed.Add(bCopyDetail);
            detailFooter.Controls.Add(bResetSelected);
            detailFooter.Controls.Add(bCopyDetail);
            Panel detailHost = new Panel();
            detailHost.Controls.Add(machineDetail);
            detailHost.Controls.Add(detailFooter);
            machineDetail.Dock = DockStyle.Fill;
            views[TabMachines] = Split(machines.Grid, detailHost, filters);

            // PXE boots
            requests.Col("Time", 140, delegate (PxeRequest r) { return Txt.Local(r.Utc); });
            requests.Col("Name", 120, delegate (PxeRequest r) { return RequestName(r); }).Fill = true;
            requests.Col("Result", 190, delegate (PxeRequest r) { return r.Served ? "Offered " + DeploymentText(r.OfferId) : r.Result; }).Fill = true;
            requests.Col("MAC address", 140, delegate (PxeRequest r) { return r.MacAddress; }).Mono = true;
            requests.Col("SMBIOS GUID", 290, delegate (PxeRequest r) { return r.SmbiosGuid; }).Mono = true;
            requests.Col("Answered for", 150, delegate (PxeRequest r) { return r.Identity; });
            requests.Col("Architecture", 90, delegate (PxeRequest r) { return r.ArchitectureText; });
            requests.Col("Server", 140, delegate (PxeRequest r) { return r.Server; });
            requests.ForeColor = delegate (PxeRequest r)
            {
                if (r.Served) return theme.OkFg;
                if (r.Outcome == RequestOutcome.Open) return Color.Empty;
                return theme.ErrFg;
            };
            requests.SelectionChanged += delegate { ShowRequest(requests.Current); };
            requests.EmptyText = "No PXE boots in SMSPXE.log in this time. Boot a machine from the network, with Watch on.";
            requests.MenuExtras = delegate (ContextMenuStrip m, PxeRequest r)
            {
                Menus.Add(m, "Show the machine", delegate { ShowMachineOf(r); });
                if (r.MacAddress != null) Menus.Add(m, "Copy MAC address", delegate { Menus.Copy(r.MacAddress); });
                if (r.SmbiosGuid != null) Menus.Add(m, "Copy SMBIOS GUID", delegate { Menus.Copy(r.SmbiosGuid); });
                Menus.Add(m, "Copy the log lines", delegate { Menus.Copy(LinesText(r)); });
            };
            requests.Grid.CellDoubleClick += delegate (object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && requests.Current != null) Safe.Run(delegate { ShowMachineOf(requests.Current); }); };
            requestFooter.Dock = DockStyle.Bottom;
            requestFooter.Height = Dpi.S(56);
            requestFooter.Padding = new Padding(Dpi.S(12), Dpi.S(12), Dpi.S(12), Dpi.S(12));
            bRequestMachine.Primary = true;
            bRequestMachine.Dock = DockStyle.Right;
            bRequestMachine.Click += Safe.Wrap(delegate { if (requests.Current != null) ShowMachineOf(requests.Current); });
            bRequestLog.Dock = DockStyle.Left;
            bRequestLog.Click += Safe.Wrap(delegate { if (requests.Current != null) PxeLogMenu(requests.Current.Server); });
            themed.Add(bRequestMachine);
            themed.Add(bRequestLog);
            requestFooter.Controls.Add(bRequestMachine);
            requestFooter.Controls.Add(bRequestLog);
            Panel requestHost = new Panel();
            requestHost.Controls.Add(requestDetail);
            requestHost.Controls.Add(requestFooter);
            requestDetail.Dock = DockStyle.Fill;
            views[TabRequests] = Split(requests.Grid, requestHost, null);

            // PXE flags
            flags.Col("Time", 140, delegate (PxeFlag f) { return Txt.Local(f.TimeUtc); });
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
            activityTitle.Text = "Changes made in this session, and the next PXE boot after each reset. Changes are also written to " + ActivityLog.FilePath + ".";
            bActivityLog.Dock = DockStyle.Right;
            bActivityLog.Click += Safe.Wrap(delegate { OpenInLogVice(ActivityLog.FilePath); });
            themed.Add(bActivityLog);
            ab.Controls.Add(activityTitle);
            ab.Controls.Add(bActivityLog);
            bars.Add(ab);
            activity.Col("Time", 150, delegate (Activity a) { return Txt.Local(a.TimeUtc); });
            activity.Col("Machine", 150, delegate (Activity a) { return a.Machine; });
            activity.Col("Change", 420, delegate (Activity a) { return a.Action; }).Fill = true;
            activity.Col("Result", 220, delegate (Activity a) { return (a.Ok ? "" : "Failed: ") + a.Result; }).Fill = true;
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
            themed.Add(requests);
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

        [DllImport("user32.dll")]
        static extern bool FlashWindow(IntPtr hwnd, bool invert);

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
                    int want = Math.Min(Dpi.S(480), Math.Max(Dpi.S(320), host.Width * 2 / 5));
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
            string[] order = Palettes.Choices;
            string cur = Theme.SavedName();
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
            foreach (RichTextBox r in new RichTextBox[] { machineDetail, requestDetail })
            {
                r.BackColor = t.Card;
                r.ForeColor = t.Text;
            }
            foreach (Panel p in new Panel[] { detailFooter, requestFooter })
            {
                p.BackColor = t.Card;
                if (p.Parent != null) p.Parent.BackColor = t.Card;
            }
            foreach (IThemed x in themed) x.ApplyTheme(t);
            bResetSelected.BackColor = bCopyDetail.BackColor = bRequestMachine.BackColor = bRequestLog.BackColor = t.Card;
            BannerColors();
            try
            {
                int dark = t.IsDark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            }
            catch (Exception) { }
            ShowMachine(machines.SelectedItems);
            ShowRequest(requests.Current);
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
        string bannerAction;

        void Banner(string text, bool error, string button)
        {
            bannerError = error;
            bannerAction = button;
            bannerText.Text = text ?? "";
            banner.Visible = !string.IsNullOrEmpty(text);
            bBanner.Visible = button != null;
            if (button != null)
            {
                bBanner.Text = button;
                bBanner.AutoWidth();
            }
            BannerColors();
        }

        void BannerClick()
        {
            if (bannerAction == "Stop waiting")
            {
                awaiting.Clear();
                Banner(null, false, null);
                return;
            }
            if (bannerAction == "Update")
            {
                ShowUpdate();
                return;
            }
            if (bannerAction == "Restart PXE service")
            {
                RestartCachePoint();
                return;
            }
            OpenSettings();
        }

        void BannerColors()
        {
            banner.BackColor = bannerError ? theme.ErrBg : theme.WarnBg;
            bannerText.ForeColor = theme.Text;
        }

        // ---------- reading ----------

        void SetWatch(bool on)
        {
            watching = on;
            bWatch.Checked = on;
            bWatch.Primary = on;
            bWatch.Text = on ? "Watching" : "Watch";
            bWatch.AutoWidth();
            bWatch.ApplyTheme(theme);
            watchTimer.Interval = Math.Max(3, Prefs.GetInt("WatchSeconds", 10)) * 1000;
            watchTimer.Enabled = on;
            Status(on ? "Watching: PxeVice reads the site and SMSPXE.log every " + (watchTimer.Interval / 1000) + " seconds. Boot the machine from the network now." : "Stopped watching.");
            if (on && !busy) Reload(true);
        }

        void Reload(bool quiet)
        {
            if (busy)
            {
                if (quiet) quietPending = true;
                else pending = true;
                return;
            }
            busy = true;
            bRefresh.Enabled = false;
            if (!quiet)
            {
                busyBar.Running = true;
                Cursor = Cursors.AppStarting;
                Status(site == null ? "Connecting to the site…" : "Reading PXE flags, Unknown device records and SMSPXE.log…");
            }
            ISite current = site;
            PxeLogStore st = store;
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
                    d = Engine.Read(s, st);
                }
                catch (Exception ex) { err = ex; }
                try { BeginInvoke((MethodInvoker)delegate { Loaded(s, d, err, connectFailed, quiet); }); }
                catch (Exception) { }
            });
        }

        void Loaded(ISite s, SiteData d, Exception err, bool connectFailed, bool quiet)
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
                    if (RunAs.Account != null) where += " as " + RunAs.Account;
                    string hint = RunAs.Account != null ? " If the password of the service account is wrong, use it again in Settings and type the password." : "";
                    Banner(where + ": " + err.Message + hint, true, "Settings");
                    Status("Reading failed: " + err.Message);
                }
                else
                {
                    if (bannerError) Banner(null, false, null);
                    Bind(d, quiet);
                }
            }
            catch (Exception ex) { Safe.Report(ex); }
            if (pending)
            {
                pending = false;
                quietPending = false;
                Reload(false);
            }
            else if (quietPending)
            {
                quietPending = false;
                Reload(true);
            }
        }

        void Bind(SiteData d, bool quiet)
        {
            data = d;
            SiteInfo info = site.Info;
            subtitle.Text = info.Describe() + " · read " + d.ReadUtc.ToLocalTime().ToString("HH:mm:ss", Inv);
            Text = "PxeVice - " + (string.IsNullOrEmpty(info.Name) ? info.SiteServer : info.Name) + " (" + info.SiteCode + ")" + (demo ? " (demo)" : "");
            // The deployments and PXE points are read again after five minutes; checks after every read.
            if (DateTime.UtcNow - cacheUtc > TimeSpan.FromMinutes(5))
            {
                cache = new CheckCache();
                cacheUtc = DateTime.UtcNow;
            }
            else
            {
                cache.Results.Clear();
                cache.Collections.Clear();
            }
            checking.Clear();

            DateTime now = DateTime.UtcNow;
            int unknown = 0, deleted = 0, records = 0, notServiced = 0, boots = 0;
            foreach (Machine m in d.Machines)
            {
                if (m.HasUnknownFlag) unknown++;
                if (m.HasDeletedFlag) deleted++;
                if (m.HasUnknownRecord) records++;
                if (m.NotServiced) notServiced++;
            }
            foreach (PxeRequest r in d.Requests) if (InWindow(r, now)) boots++;
            cMachines.Count = d.Machines.Count;
            cNotServiced.Count = notServiced;
            cRequests.Count = boots;
            cFlags.Count = d.Flags.Count;
            cUnknown.Count = unknown;
            cRecords.Count = records;
            cDeleted.Count = deleted;
            foreach (Control c in chips.Controls) c.Invalidate();
            BindMachines();
            BindRequests();
            StringBuilder fs = new StringBuilder();
            foreach (PxeFlag x in d.Flags) fs.Append(x.MachineId).Append('|').Append(x.RawMac).Append('|').Append(x.DeploymentId).Append('|').Append(Txt.Local(x.TimeUtc)).Append(';');
            if (fs.ToString() != flagSignature)
            {
                flagSignature = fs.ToString();
                flags.SetRows(new List<PxeFlag>(d.Flags));
            }
            tabs.SetText(TabFlags, "PXE flags (" + d.Flags.Count + ")");

            string news = NewRequests(d, quiet);
            string next = NextBoots(d);
            if (next != null) Status(next);
            else if (news != null) Status(news);
            else if (pendingDoneMessage != null)
            {
                Status(pendingDoneMessage);
                pendingDoneMessage = null;
            }
            else if (!quiet)
            {
                List<string> notes = new List<string>(info.Notes);
                notes.AddRange(d.Notes);
                if (notes.Count > 0) Status(string.Join(" ", notes.ToArray()));
                else Status(d.Machines.Count == 0 ? "Nothing is left behind." : d.Machines.Count + (d.Machines.Count == 1 ? " machine needs" : " machines need") + " a look. Select one to see why it does not PXE boot.");
            }
            firstRead = false;
            WaitBanner();
        }

        bool InWindow(PxeRequest r, DateTime now)
        {
            return !r.Utc.HasValue || now - r.Utc.Value <= Engine.RequestWindow;
        }

        static string RequestId(PxeRequest r)
        {
            return r.Server + "|" + r.Thread + "|" + (r.Utc.HasValue ? r.Utc.Value.Ticks.ToString(Inv) : "") + "|" + r.MacAddress;
        }

        // Says which PXE boots came in since the last read. Requests still being written are announced once they have an answer.
        string NewRequests(SiteData d, bool quiet)
        {
            List<PxeRequest> fresh = new List<PxeRequest>();
            foreach (PxeRequest r in d.Requests)
            {
                if (r.Outcome == RequestOutcome.Open) continue;
                string id = RequestId(r);
                if (seen.ContainsKey(id)) continue;
                seen[id] = true;
                if (!firstRead) fresh.Add(r);
            }
            if (fresh.Count == 0) return null;
            PxeRequest n = fresh[0];
            if (quiet && !ContainsFocus) try { FlashWindow(Handle, true); } catch (Exception) { }
            return "New PXE boot" + (fresh.Count > 1 ? "s (" + fresh.Count + ")" : "") + ": " + RequestName(n) + " " + (n.MacAddress ?? "") + " on " + n.Server + " at " + Txt.Local(n.Utc).Substring(11) + ": " + (n.Served ? "offered " + DeploymentText(n.OfferId) : n.Result.ToLowerInvariant()) + ".";
        }

        // Tells when a machine that was reset has PXE booted again, and what it got.
        string NextBoots(SiteData d)
        {
            string msg = null;
            for (int i = awaiting.Count - 1; i >= 0; i--)
            {
                Awaited w = awaiting[i];
                PxeRequest hit = null;
                foreach (PxeRequest r in d.Requests)
                {
                    if (r.Outcome == RequestOutcome.Open || !r.Utc.HasValue || r.Utc.Value < w.Since) continue;
                    bool match = (w.Guid != null && r.SmbiosGuid == w.Guid) || (w.Mac != null && r.MacAddress == w.Mac && (w.Guid == null || r.SmbiosGuid == null));
                    if (match) { hit = r; break; }
                }
                if (hit == null) continue;
                awaiting.RemoveAt(i);
                bool remembered = hit.ItemKey >= 0 && w.Deleted.Contains(hit.ItemKey);
                Activity a = new Activity();
                a.TimeUtc = hit.Utc ?? DateTime.UtcNow;
                a.Machine = w.Name;
                a.Action = "Next PXE boot on " + hit.Server;
                a.Ok = hit.Served;
                a.Result = hit.Served ? "Offered " + DeploymentText(hit.OfferId) : hit.Result + ". See the PXE check";
                if (remembered) a.Result = "Answered for the deleted device " + hit.ItemKey + ": the PXE point still remembers it";
                a.User = Cli.UserName;
                activities.Insert(0, a);
                ActivityLog.Write(a);
                activity.SetRows(new List<Activity>(activities));
                tabs.SetText(TabActivity, "Activity (" + activities.Count + ")");
                msg = hit.Served ? w.Name + " PXE booted again and was offered " + DeploymentText(hit.OfferId) + "." : w.Name + " PXE booted again, but was not serviced. Select it to see why.";
                if (remembered)
                {
                    // The site let the device go, but the PXE point answers from what it remembers.
                    msg = w.Name + " PXE booted again, but " + hit.Server + " still answered for the device that was deleted (" + hit.ItemKey + "). The PXE point remembers devices for a while.";
                    cacheServer = hit.Server;
                    Banner("The PXE point on " + hit.Server + " still remembers the deleted device " + w.Name + ". " + (w.Restarted ? "The PXE service was restarted before this boot, so wait a minute and boot it again." : "Restart the PXE service to make it forget, then boot it again."), true, w.Restarted ? null : "Restart PXE service");
                    awaiting.Add(NewWait(w));
                }
                try { if (!ContainsFocus) FlashWindow(Handle, true); } catch (Exception) { }
            }
            return msg;
        }

        string cacheServer;

        // The same machine, waiting for one more boot.
        static Awaited NewWait(Awaited w)
        {
            Awaited n = new Awaited();
            n.Key = w.Key;
            n.Name = w.Name;
            n.Mac = w.Mac;
            n.Guid = w.Guid;
            n.Since = DateTime.UtcNow;
            n.Restarted = w.Restarted;
            n.Deleted.AddRange(w.Deleted);
            return n;
        }

        void RestartCachePoint()
        {
            string server = cacheServer;
            ISite s = site;
            if (server == null || s == null || busy) return;
            busy = true;
            busyBar.Running = true;
            Status("Restarting the PXE service on " + server + "…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Activity> done = new List<Activity>();
                try
                {
                    foreach (string svc in s.RunningPxeServices(server))
                    {
                        Activity a = new Activity();
                        a.TimeUtc = DateTime.UtcNow;
                        a.Machine = server;
                        a.Action = "Restart the service " + svc + " on " + server;
                        a.User = Cli.UserName;
                        try { s.RestartService(server, svc); a.Ok = true; a.Result = "Restarted"; }
                        catch (Exception ex) { a.Ok = false; a.Result = ex.Message; }
                        ActivityLog.Write(a);
                        done.Add(a);
                    }
                }
                catch (Exception ex)
                {
                    Activity a = new Activity();
                    a.TimeUtc = DateTime.UtcNow;
                    a.Machine = server;
                    a.Action = "Restart the PXE service on " + server;
                    a.User = Cli.UserName;
                    a.Ok = false;
                    a.Result = ex.Message;
                    ActivityLog.Write(a);
                    done.Add(a);
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        busy = false;
                        busyBar.Running = false;
                        bool ok = done.Count > 0;
                        foreach (Activity a in done) if (!a.Ok) ok = false;
                        done.Reverse();
                        activities.InsertRange(0, done);
                        activity.SetRows(new List<Activity>(activities));
                        tabs.SetText(TabActivity, "Activity (" + activities.Count + ")");
                        foreach (Awaited w in awaiting) w.Restarted = ok;
                        bannerError = false;
                        Banner(null, false, null);
                        Status(ok ? "The PXE service was restarted on " + server + ". Boot the machine again." : "The PXE service could not be restarted on " + server + ". See the Activity tab.");
                        WaitBanner();
                    });
                }
                catch (Exception) { }
            });
        }

        void WaitBanner()
        {
            if (bannerError) return;
            if (awaiting.Count == 0)
            {
                if (bannerAction == "Stop waiting") Banner(null, false, null);
                return;
            }
            List<string> names = new List<string>();
            foreach (Awaited w in awaiting) names.Add(w.Name);
            Banner("Waiting for the next PXE boot of " + string.Join(", ", names.ToArray()) + (watching ? ". Restart it and boot from the network." : ". Turn on Watch, then boot it from the network."), false, "Stop waiting");
        }

        void Filter(string f)
        {
            chipFilter = f;
            cMachines.Selected = f == null && data != null;
            cNotServiced.Selected = f == FilterNotServiced;
            cUnknown.Selected = f == FilterUnknown;
            cRecords.Selected = f == FilterRecords;
            cDeleted.Selected = f == FilterDeleted;
            foreach (Control c in chips.Controls) c.Invalidate();
            tabs.Selected = TabMachines;
            BindMachines();
        }

        string machineSignature = "", requestSignature = "", flagSignature = "";

        void BindMachines()
        {
            if (data == null) return;
            string q = search.Text.Trim();
            List<Machine> list = new List<Machine>();
            StringBuilder sig = new StringBuilder();
            foreach (Machine m in data.Machines)
            {
                if (chipFilter == FilterNotServiced && !m.NotServiced) continue;
                if (chipFilter == FilterUnknown && !m.HasUnknownFlag) continue;
                if (chipFilter == FilterRecords && !m.HasUnknownRecord) continue;
                if (chipFilter == FilterDeleted && !m.HasDeletedFlag) continue;
                if (q.Length > 0 && !Matches(m, q)) continue;
                list.Add(m);
                sig.Append(m.Key).Append('|').Append(m.Found).Append('|').Append(Txt.Local(m.LastSeenUtc)).Append('|').Append(m.Requests.Count).Append(';');
            }
            List<string> keys = new List<string>();
            foreach (Machine m in machines.SelectedItems) keys.Add(m.Key);
            if (sig.ToString() != machineSignature)
            {
                machineSignature = sig.ToString();
                machines.SetRows(list);
                foreach (Machine m in list) if (keys.Count > 0 && m.Key == keys[0]) { machines.Select(m); break; }
            }
            else
            {
                // The same machines: keep the rows and the selection, and take the new objects.
                List<Machine> rows = machines.Rows;
                for (int i = 0; i < rows.Count && i < list.Count; i++) rows[i] = list[i];
            }
            tabs.SetText(TabMachines, list.Count == data.Machines.Count ? "Machines (" + list.Count + ")" : "Machines (" + list.Count + " of " + data.Machines.Count + ")");
            ShowMachine(machines.SelectedItems);
        }

        void BindRequests()
        {
            if (data == null) return;
            string q = search.Text.Trim();
            DateTime now = DateTime.UtcNow;
            List<PxeRequest> list = new List<PxeRequest>();
            StringBuilder sig = new StringBuilder();
            foreach (PxeRequest r in data.Requests)
            {
                if (!InWindow(r, now)) continue;
                if (q.Length > 0 && !RequestMatches(r, q)) continue;
                list.Add(r);
                sig.Append(RequestId(r)).Append('|').Append(r.Outcome).Append(';');
            }
            PxeRequest cur = requests.Current;
            string curId = cur == null ? null : RequestId(cur);
            if (sig.ToString() != requestSignature)
            {
                requestSignature = sig.ToString();
                requests.SetRows(list);
                if (curId != null) foreach (PxeRequest r in list) if (RequestId(r) == curId) { requests.Select(r); break; }
            }
            int bad = list.FindAll(delegate (PxeRequest r) { return !r.Served && r.Outcome != RequestOutcome.Open; }).Count;
            tabs.SetText(TabRequests, "PXE boots (" + list.Count + (bad > 0 ? ", " + bad + " not serviced" : "") + ")");
            ShowRequest(requests.Current);
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

        bool RequestMatches(PxeRequest r, string q)
        {
            string mac = Ids.Mac(q);
            if (mac != null) return r.MacAddress == mac;
            return Has(r.MacAddress, q) || Has(r.SmbiosGuid, q) || Has(RequestName(r), q) || Has(r.OfferId, q) || Has(DeploymentName(r.OfferId), q) || Has(r.Server, q) || Has(r.Result, q);
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

        string DeploymentText(string id)
        {
            string n = DeploymentName(id);
            return n.Length > 0 ? id + " (" + n + ")" : id;
        }

        Machine MachineOf(PxeRequest r)
        {
            if (data == null) return null;
            foreach (Machine m in data.Machines) if (m.Requests.Contains(r)) return m;
            return null;
        }

        string RequestName(PxeRequest r)
        {
            if (!string.IsNullOrEmpty(r.Netbios)) return r.Netbios;
            Machine m = MachineOf(r);
            if (m != null) return m.Name;
            return r.IsUnknownComputer ? "Unknown computer" : r.ItemKey > 0 ? "Device " + r.ItemKey.ToString(Inv) : "";
        }

        void ShowView()
        {
            for (int i = 0; i < views.Length; i++) views[i].Visible = i == tabs.Selected;
            views[tabs.Selected].BringToFront();
        }

        void ShowMachineOf(PxeFlag f)
        {
            if (data == null) return;
            foreach (Machine m in data.Machines) if (m.Flags.Contains(f)) { Reveal(m); return; }
        }

        void ShowMachineOf(PxeRequest r)
        {
            Machine m = MachineOf(r);
            if (m != null)
            {
                Reveal(m);
                return;
            }
            Status(r.Served ? "This boot was served, so the machine is not on the Machines tab." : "The machine is not on the Machines tab. Its last boot may be older than the time set in Settings.");
        }

        void Reveal(Machine m)
        {
            search.Text = "";
            searchTimer.Stop();
            Filter(null);
            foreach (Machine x in machines.Rows)
            {
                if (x.Key == m.Key)
                {
                    machines.Select(x);
                    machines.Grid.Focus();
                    return;
                }
            }
        }

        void ShowRequestsOf(Machine m)
        {
            search.Text = m.MacAddress ?? m.SmbiosGuid ?? "";
            searchTimer.Stop();
            BindMachines();
            BindRequests();
            tabs.Selected = TabRequests;
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

        void Plain(RichTextBox r, string text, Color c)
        {
            r.SelectionFont = Fonts.Ui;
            r.SelectionColor = c;
            r.AppendText(text + "\n");
        }

        void Small(RichTextBox r, string text, Color c, bool mono)
        {
            r.SelectionFont = mono ? Fonts.Mono : Fonts.Small;
            r.SelectionColor = c;
            r.AppendText(text + "\n");
        }

        Color SeverityColor(Severity s)
        {
            switch (s)
            {
                case Severity.Blocks: return theme.ErrFg;
                case Severity.Warning: return theme.WarnFg;
                case Severity.Ok: return theme.OkFg;
            }
            return theme.Text3;
        }

        void Findings(RichTextBox r, List<Finding> list)
        {
            foreach (Finding f in list)
            {
                r.SelectionFont = Fonts.SmallSemibold;
                r.SelectionColor = SeverityColor(f.Severity);
                r.AppendText(f.SeverityText.ToUpperInvariant() + "   ");
                r.SelectionFont = Fonts.UiSemibold;
                r.SelectionColor = theme.Text;
                r.AppendText(f.Title + "\n");
                if (!string.IsNullOrEmpty(f.What)) Plain(r, f.What, theme.Text2);
                if (!string.IsNullOrEmpty(f.ToDo))
                {
                    r.SelectionFont = Fonts.UiSemibold;
                    r.SelectionColor = theme.Text;
                    r.AppendText("To do: ");
                    Plain(r, f.ToDo, theme.Text);
                }
                int shown = 0;
                foreach (string e in f.Evidence)
                {
                    if (shown++ >= 6)
                    {
                        Small(r, "… " + (f.Evidence.Count - 6) + " more lines in the report", theme.Text3, false);
                        break;
                    }
                    Small(r, Txt.Short(e, 150), theme.Text3, true);
                }
                r.AppendText("\n");
            }
        }

        void ShowMachine(List<Machine> sel)
        {
            RichTextBox r = machineDetail;
            int keep = r.SelectionStart;
            string before = r.Text;
            r.SuspendLayout();
            r.Clear();
            r.SelectionIndent = Dpi.S(12);
            r.SelectionRightIndent = Dpi.S(8);
            int n = sel == null ? 0 : sel.Count;
            bResetSelected.Enabled = n > 0 && !busy;
            bReset.Enabled = n > 0 && !busy;
            bCopyDetail.Enabled = n == 1;
            bResetSelected.Text = n > 1 ? "Reset " + n + " machines" : "Reset";
            bResetSelected.AutoWidth();
            if (n == 0)
            {
                r.AppendText("\n");
                Plain(r, data == null ? "Reading the site…" : data.Machines.Count == 0 ? "Nothing is left behind, and no PXE boot went unserviced." : "Select a machine to see why it does not PXE boot.", theme.Text3);
            }
            else if (n > 1)
            {
                r.AppendText("\n");
                Heading(r, n + " machines");
                foreach (Machine m in sel)
                {
                    r.SelectionFont = Fonts.UiSemibold;
                    r.SelectionColor = theme.Text;
                    r.AppendText(m.Name + "  ");
                    Plain(r, m.Found + (m.MacAddress != null ? "  ·  " + m.MacAddress : ""), theme.Text2);
                }
                Section(r, "Reset");
                Plain(r, "PxeVice shows every change for these machines before it makes them.", theme.Text2);
            }
            else Describe(r, sel[0]);
            // A refresh while watching shows the same text: stay where the reader was.
            if (r.Text == before && keep > 0 && keep < r.TextLength) r.SelectionStart = keep;
            else r.SelectionStart = 0;
            r.ScrollToCaret();
            r.ResumeLayout();
        }

        void StartCheck(Machine m)
        {
            if (site == null || m.Key == null || checking.ContainsKey(m.Key)) return;
            checking[m.Key] = true;
            ISite s = site;
            CheckCache c = cache;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Finding> f;
                try { f = Checker.Check(s, m, c, DateTime.UtcNow); }
                catch (Exception ex)
                {
                    f = new List<Finding>();
                    f.Add(Finding.Make(Severity.Info, "The check did not finish", ex.Message, null));
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (c != cache) return;
                        c.Results[m.Key] = f;
                        List<Machine> sel = machines.SelectedItems;
                        if (sel.Count == 1 && sel[0].Key == m.Key) ShowMachine(sel);
                    });
                }
                catch (Exception) { }
            });
        }

        void Describe(RichTextBox r, Machine m)
        {
            DateTime now = DateTime.UtcNow;
            r.AppendText("\n");
            Heading(r, m.Name);
            Plain(r, m.Kind + (m.LastSeenUtc.HasValue ? "  ·  last seen " + Txt.Ago(m.LastSeenUtc, now) : ""), theme.Text3);
            foreach (Awaited w in awaiting)
            {
                if (w.Key != m.Key) continue;
                r.AppendText("\n");
                Small(r, "WAITING FOR THE NEXT PXE BOOT since " + Txt.Local(w.Since).Substring(11) + (watching ? "" : ". Turn on Watch to see it come in."), theme.Accent, false);
            }

            Section(r, "PXE check");
            List<Finding> f;
            if (cache.Results.TryGetValue(m.Key ?? "", out f)) Findings(r, f);
            else
            {
                Plain(r, "Checking the deployments, collections and boot images for this machine…", theme.Text3);
                StartCheck(m);
            }

            Section(r, "Machine");
            Field(r, "MAC addresses", string.Join("\n", m.AllMacs.ToArray()), true);
            Field(r, "SMBIOS GUID", m.SmbiosGuid, true);
            List<string> devs = new List<string>();
            foreach (DeviceRecord d in m.Devices) devs.Add(d.Name + " (" + d.ResourceId.ToString(Inv) + ")" + (d.CreatedUtc.HasValue ? ", created " + Txt.Local(d.CreatedUtc) : ""));
            foreach (PxeFlag x in m.Flags) if (x.DeviceName != null && !devs.Exists(delegate (string s) { return s.StartsWith(x.DeviceName + " ("); })) devs.Add(x.DeviceName + " (" + x.MachineId.ToString(Inv) + ")");
            Field(r, "Device records", string.Join("\n", devs.ToArray()), false);
            List<string> ids = new List<string>();
            foreach (PxeFlag x in m.Flags) ids.Add(x.DeploymentId + " · MachineID " + x.MachineId.ToString(Inv) + (x.IsUnknownComputer ? " (Unknown Computer)" : x.IsDeletedDevice ? " (deleted)" : "") + " · " + Txt.Local(x.TimeUtc));
            Field(r, "PXE flags", string.Join("\n", ids.ToArray()), false);

            if (m.Requests.Count > 0)
            {
                Section(r, "PXE boots");
                int shown = 0;
                foreach (PxeRequest q in m.Requests)
                {
                    if (shown++ >= 5) break;
                    r.SelectionFont = Fonts.Ui;
                    r.SelectionColor = theme.Text;
                    r.AppendText(Txt.Local(q.Utc) + "  ");
                    r.SelectionColor = q.Served ? theme.OkFg : q.Outcome == RequestOutcome.Open ? theme.Text3 : theme.ErrFg;
                    r.AppendText((q.Served ? "Offered " + DeploymentText(q.OfferId) : q.Result) + "\n");
                    Small(r, q.Server + (q.ArchitectureText.Length > 0 ? " · " + q.ArchitectureText : "") + (q.Identity.Length > 0 ? " · " + q.Identity : ""), theme.Text3, false);
                }
            }

            Section(r, "Reset will");
            bool deleteUnknown = Prefs.GetBool("DeleteUnknown", true);
            int steps = 0;
            if (m.HasNamedDevice || m.HasDeviceFlag || m.Devices.Count > 0) { Plain(r, "· Clear the PXE flag with Clear Required PXE Deployments", theme.Text); steps++; }
            if (m.HasUnknownRecord && deleteUnknown) { Plain(r, "· Delete the Unknown device record", theme.Text); steps++; }
            if (m.Flags.Count > 0) { Plain(r, "· Delete this machine's rows in LastPXEAdvertisement", theme.Text); steps++; }
            if (steps == 0) Plain(r, "Nothing. What blocks this machine is in the PXE check above.", theme.Text2);
            else Plain(r, "Then PxeVice waits for the next PXE boot and tells what it got.", theme.Text3);
        }

        void ShowRequest(PxeRequest q)
        {
            RichTextBox r = requestDetail;
            r.SuspendLayout();
            r.Clear();
            r.SelectionIndent = Dpi.S(12);
            r.SelectionRightIndent = Dpi.S(8);
            bRequestMachine.Enabled = q != null && MachineOf(q) != null;
            bRequestLog.Enabled = q != null;
            if (q == null)
            {
                r.AppendText("\n");
                Plain(r, data == null ? "Reading SMSPXE.log…" : "Select a PXE boot to see what the PXE point answered, and the log lines behind it.", theme.Text3);
            }
            else
            {
                DateTime now = DateTime.UtcNow;
                r.AppendText("\n");
                Heading(r, RequestName(q).Length > 0 ? RequestName(q) : q.MacAddress);
                r.SelectionFont = Fonts.UiSemibold;
                r.SelectionColor = q.Served ? theme.OkFg : q.Outcome == RequestOutcome.Open ? theme.Text3 : theme.ErrFg;
                r.AppendText((q.Served ? "Offered " + DeploymentText(q.OfferId) : q.Result) + "\n");
                Plain(r, Txt.Local(q.Utc) + " (" + Txt.Ago(q.Utc, now) + ") on " + q.Server, theme.Text3);
                Section(r, "What the PXE point saw");
                Field(r, "MAC address", q.MacAddress, true);
                Field(r, "SMBIOS GUID", q.SmbiosGuid, true);
                Field(r, "Architecture", q.ArchitectureText + (q.Architecture.HasValue ? " (" + q.Architecture.Value.ToString(Inv) + ")" : ""), false);
                Field(r, "Answered for", q.ItemKey < 0 ? q.Identity : (q.IsUnknownComputer ? "The Unknown Computer object " : "The device ") + q.ItemKey.ToString(Inv) + (q.Netbios != null ? " (" + q.Netbios + ")" : ""), false);
                if (q.DuplicateSmbios || q.DuplicateMac) Field(r, "Duplicates", (q.DuplicateSmbios ? "More than one record has this SMBIOS GUID. " : "") + (q.DuplicateMac ? "More than one record has this MAC address." : ""), false);
                if (q.Served)
                {
                    Field(r, "Deployment", DeploymentText(q.OfferId) + (q.Mandatory == true ? ", required" : q.Mandatory == false ? ", available" : ""), false);
                    Field(r, "Task sequence and boot image", (q.PackageId ?? "") + (q.BootImageId != null ? " · boot image " + q.BootImageId : ""), false);
                }
                else if (q.Outcome != RequestOutcome.Open)
                {
                    Section(r, "Why");
                    Plain(r, MachineOf(q) != null ? "Show the machine for the PXE check: what blocks it and what to do." : "The PXE point found no deployment for it.", theme.Text2);
                }
                Section(r, "Log lines");
                foreach (LogLine l in q.Lines) Small(r, Txt.Local(l.Utc).Substring(11) + "  " + l.Text, theme.Text2, true);
            }
            r.SelectionStart = 0;
            r.ScrollToCaret();
            r.ResumeLayout();
        }

        static string LinesText(PxeRequest r)
        {
            StringBuilder b = new StringBuilder();
            foreach (LogLine l in r.Lines) b.Append(Txt.Local(l.Utc)).Append("  ").Append(l.Text).Append("\r\n");
            return b.ToString();
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
            Run(s, chosen, sel);
        }

        void Run(ISite s, Plan plan, List<Machine> sel)
        {
            busy = true;
            busyBar.Running = true;
            bRefresh.Enabled = false;
            ShowMachine(machines.SelectedItems);
            Cursor = Cursors.AppStarting;
            Status("Resetting…");
            DateTime started = DateTime.UtcNow;
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
                try { BeginInvoke((MethodInvoker)delegate { Ran(done, err, sel, started, plan); }); }
                catch (Exception) { }
            });
        }

        void Ran(List<Activity> done, Exception err, List<Machine> sel, DateTime started, Plan plan)
        {
            busy = false;
            busyBar.Running = false;
            bRefresh.Enabled = true;
            Cursor = Cursors.Default;
            if (done != null)
            {
                // Newest first, like the PXE boots.
                List<Activity> newest = new List<Activity>(done);
                newest.Reverse();
                activities.InsertRange(0, newest);
                activity.SetRows(new List<Activity>(activities));
            }
            int failed = 0;
            if (done != null) foreach (Activity a in done) if (!a.Ok) failed++;
            tabs.SetText(TabActivity, "Activity (" + activities.Count + ")");
            if (done != null && done.Count > 0 && failed < done.Count)
            {
                foreach (Machine m in sel)
                {
                    awaiting.RemoveAll(delegate (Awaited x) { return x.Key == m.Key; });
                    Awaited w = new Awaited();
                    w.Key = m.Key;
                    w.Name = m.Name;
                    w.Mac = m.MacAddress;
                    w.Guid = m.SmbiosGuid;
                    w.Since = started;
                    if (plan != null)
                    {
                        foreach (Step st in plan.Steps)
                        {
                            if (st.Kind == StepKind.DeleteDevice && st.MachineName == m.Name) foreach (uint id in st.ResourceIds) w.Deleted.Add(id);
                            if (st.Kind == StepKind.RestartService) w.Restarted = true;
                        }
                    }
                    awaiting.Add(w);
                }
                if (!watching && Prefs.GetBool("WatchAfterReset", true)) SetWatch(true);
            }
            if (err != null) Status("The reset stopped: " + err.Message);
            else if (failed > 0)
            {
                Status(failed + (failed == 1 ? " change" : " changes") + " failed. See the Activity tab.");
                tabs.Selected = TabActivity;
            }
            else pendingDoneMessage = done == null || done.Count == 0 ? "Nothing was changed." : "Done: " + done.Count + (done.Count == 1 ? " change" : " changes") + ". Now boot the machine from the network: PxeVice tells what it gets.";
            Reload(false);
        }

        string pendingDoneMessage;

        // ---------- export ----------

        void ExportMenu()
        {
            ContextMenuStrip m = Menus.New(theme);
            Menus.Add(m, "Report of the selected machines for the ticket (HTML)…   Ctrl+E", delegate { ExportReport(); }).Enabled = machines.SelectedItems.Count > 0;
            Menus.Add(m, "Report of every machine on the list (HTML)…", delegate { ExportReport(machines.Rows); }).Enabled = machines.Rows.Count > 0;
            Menus.Add(m, "Copy the check of the selected machine as text", delegate { CopyCheck(); }).Enabled = machines.SelectedItems.Count == 1;
            Menus.Separator(m);
            Menus.Add(m, "PXE boots as CSV…", delegate { ExportRequests(); }).Enabled = data != null && data.Requests.Count > 0;
            m.Show(bExport, new Point(0, bExport.Height));
        }

        void CopyCheck()
        {
            List<Machine> sel = machines.SelectedItems;
            if (sel.Count != 1) return;
            List<Finding> f;
            if (!cache.Results.TryGetValue(sel[0].Key ?? "", out f))
            {
                Status("The check is still running. Try again in a moment.");
                return;
            }
            Status(Menus.Copy(Checker.AsText(sel[0], f, DateTime.UtcNow)) ? "The check is on the clipboard, ready for the ticket." : "The clipboard is busy. Try again.");
        }

        void ExportReport()
        {
            ExportReport(machines.SelectedItems);
        }

        void ExportReport(List<Machine> list)
        {
            if (site == null || list.Count == 0) return;
            string file;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "HTML report|*.html";
                string first = list.Count == 1 ? list[0].Name : list.Count + " machines";
                foreach (char ch in Path.GetInvalidFileNameChars()) first = first.Replace(ch, '-');
                d.FileName = "PxeVice " + first + " " + DateTime.Now.ToString("yyyy-MM-dd HHmm", Inv) + ".html";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                file = d.FileName;
            }
            ISite s = site;
            CheckCache c = cache;
            List<Activity> acts = new List<Activity>();
            foreach (Activity a in activities) foreach (Machine m in list) if (a.Machine == m.Name) { acts.Add(a); break; }
            busyBar.Running = true;
            Status("Checking and writing the report…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception err = null;
                try
                {
                    Dictionary<string, List<Finding>> checks = new Dictionary<string, List<Finding>>(StringComparer.OrdinalIgnoreCase);
                    foreach (Machine m in list) checks[m.Key ?? ""] = Checker.Check(s, m, c, DateTime.UtcNow);
                    string title = list.Count == 1 ? "PXE check of " + list[0].Name : "PXE check of " + list.Count + " machines";
                    File.WriteAllText(file, Report.Html(s.Info, list, checks, acts, DateTime.UtcNow, title), new UTF8Encoding(false));
                }
                catch (Exception ex) { err = ex; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        busyBar.Running = busy;
                        if (err != null) Status("The report was not saved: " + err.Message);
                        else
                        {
                            Status("Saved " + file);
                            OpenPath(file);
                        }
                    });
                }
                catch (Exception) { }
            });
        }

        void ExportRequests()
        {
            if (data == null) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "CSV|*.csv";
                d.FileName = "PxeVice PXE boots " + DateTime.Now.ToString("yyyy-MM-dd HHmm", Inv) + ".csv";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, Report.RequestsCsv(requests.Rows, data.DeploymentNames), new UTF8Encoding(true));
                    Status("Saved " + d.FileName);
                }
                catch (Exception ex) { Status("Not saved: " + ex.Message); }
            }
        }

        // ---------- sites ----------

        void SiteMenu()
        {
            if (demo)
            {
                Status("The demo has one site. Start PxeVice without --demo to use your own.");
                return;
            }
            ContextMenuStrip m = Menus.New(theme);
            string current = Prefs.Get("SiteName", null);
            List<SavedSite> all = Sites.All();
            foreach (SavedSite s in all)
            {
                SavedSite site2 = s;
                ToolStripMenuItem it = Menus.Add(m, s.Name + "   " + s.Describe(), delegate { UseSite(site2); });
                it.Checked = string.Equals(s.Name, current, StringComparison.OrdinalIgnoreCase);
            }
            if (all.Count > 0) Menus.Separator(m);
            SavedSite here = new SavedSite();
            here.Name = null;
            Menus.Add(m, "This computer", delegate { UseSite(here); }).Checked = current == null && string.IsNullOrEmpty(Prefs.Get("SiteServer", null));
            Menus.Add(m, "Save this site as…", delegate { SaveSite(); }).Enabled = site != null;
            Menus.Add(m, "Sites in Settings…", delegate { OpenSettings(); });
            m.Show(bSite, new Point(0, bSite.Height));
        }

        void UseSite(SavedSite s)
        {
            Sites.Use(s);
            Reconnect();
            Status("Connecting to " + (s.Name ?? "this computer") + "…");
        }

        void SaveSite()
        {
            if (site == null) return;
            using (NameDialog d = new NameDialog(theme, "Save this site", "Name", "A name for " + site.Info.SiteServer + " (site " + site.Info.SiteCode + "), such as the customer.", site.Info.Name ?? site.Info.SiteCode))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Value.Length == 0) return;
                SavedSite s = Sites.Current();
                s.Name = d.Value;
                if (string.IsNullOrEmpty(s.SiteServer)) s.SiteServer = site.Info.SiteServer;
                Sites.Save(s);
                Prefs.Set("SiteName", s.Name);
                site.Info.Name = s.Name;
                subtitle.Text = site.Info.Describe();
                Status("Saved the site as " + s.Name + ".");
            }
        }

        void Reconnect()
        {
            if (site != null) site.Dispose();
            site = null;
            data = null;
            store = new PxeLogStore();
            cache = new CheckCache();
            cacheUtc = DateTime.MinValue;
            seen.Clear();
            firstRead = true;
            awaiting.Clear();
            machineSignature = requestSignature = flagSignature = "";
            machines.SetRows(new List<Machine>());
            requests.SetRows(new List<PxeRequest>());
            flags.SetRows(new List<PxeFlag>());
            Banner(null, false, null);
            subtitle.Text = "Connecting…";
            Reload(false);
        }

        // ---------- LogVice ----------

        void PxeLogMenu(string only)
        {
            if (site == null)
            {
                Status("Connect to the site first.");
                return;
            }
            List<string> servers = only != null ? new List<string>(new string[] { only }) : site.Info.PxeServers;
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
            if (s.Info.Demo)
            {
                Status("The demo has no SMSPXE.log to open. Its lines are on the PXE boots tab.");
                return;
            }
            Status("Looking for SMSPXE.log on " + server + "…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string path = null;
                bool copy = false;
                try
                {
                    path = s.PxeLog(server);
                    // LogVice runs with your own account. With a service account, PxeVice copies the log here with it.
                    if (path != null && RunAs.Account != null)
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "PxeVice");
                        Directory.CreateDirectory(dir);
                        string local = Path.Combine(dir, server.Split('.')[0] + " SMSPXE.log");
                        using (RunAs.Impersonate())
                        using (FileStream src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (FileStream dst = new FileStream(local, FileMode.Create, FileAccess.Write))
                            src.CopyTo(dst);
                        path = local;
                        copy = true;
                    }
                }
                catch (Exception) { }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (path == null) Status("SMSPXE.log was not found on " + server + ". Set where it is in Settings.");
                        else
                        {
                            OpenInLogVice(path);
                            if (copy) Status("Opened a copy of SMSPXE.log from " + server + ", read with " + RunAs.Account + ". Open it again for newer lines.");
                        }
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
                    theme = Theme.Named(Theme.SavedName());
                    ApplyTheme();
                };
                d.ShowDialog(this);
                if (d.UseAccountNow && !demo)
                {
                    if (string.IsNullOrEmpty(Prefs.Get(RunAs.UserPref, null)))
                    {
                        RunAs.Stop();
                        Status("No service account is set, so PxeVice uses your own account.");
                    }
                    else if (Program.UseAccount(this, true)) Status("Connecting as " + RunAs.Account + "…");
                    Reconnect();
                    return;
                }
                Engine.RequestWindow = TimeSpan.FromHours(Math.Max(1, Prefs.GetInt("RequestHours", 24)));
                if (watching) watchTimer.Interval = Math.Max(3, Prefs.GetInt("WatchSeconds", 10)) * 1000;
                requestSignature = "";
                if (d.SiteChanged && !demo)
                {
                    // Changes to the site in use go into its saved copy as well.
                    SavedSite cur = Sites.Current();
                    if (!string.IsNullOrEmpty(cur.Name) && Sites.Find(cur.Name) != null) Sites.Save(cur);
                    Reconnect();
                }
                else if (data != null) Reload(false);
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
                    case Keys.F5: Reload(false); return true;
                    case Keys.F1: About(); return true;
                    case Keys.Control | Keys.W: SetWatch(!watching); return true;
                    case Keys.Control | Keys.R: ResetSelected(); return true;
                    case Keys.Control | Keys.E: ExportReport(); return true;
                    case Keys.Control | Keys.L: PxeLogMenu(null); return true;
                    case Keys.Control | Keys.Oemcomma: OpenSettings(); return true;
                    case Keys.Control | Keys.D1: tabs.Selected = TabMachines; return true;
                    case Keys.Control | Keys.D2: tabs.Selected = TabRequests; return true;
                    case Keys.Control | Keys.D3: tabs.Selected = TabFlags; return true;
                    case Keys.Control | Keys.D4: tabs.Selected = TabActivity; return true;
                    case Keys.Control | Keys.F:
                        tabs.Selected = TabMachines;
                        search.Focus();
                        search.SelectAll();
                        return true;
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
            watchTimer.Enabled = false;
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
