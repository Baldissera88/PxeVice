namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;

    // Settings: the subjects on the left, one page on the right and a search over all of them.
    public sealed class SettingsDialog : PageForm
    {
        public event EventHandler ThemeChanged;
        public bool SiteChanged;
        public bool UseAccountNow;
        // Off for screenshots, so the subject shown is not saved.
        public static bool RememberPage = true;

        static readonly string[] PageGlyphs = { Glyphs2.Settings, Glyphs3.Server, Glyphs.Shield, Glyphs.Watch, Glyphs3.Delete, Glyphs2.Sync, Glyphs2.Folder, Glyphs.Log, Glyphs.Info };

        readonly ISettingStore store = new PrefsSettingStore();
        readonly SiteInfo info;
        readonly SubjectList subjects;
        readonly Panel head = new Panel();
        readonly TextBox search = new TextBox();
        readonly Dictionary<string, List<Control>> built = new Dictionary<string, List<Control>>();
        readonly Dictionary<string, SettingsHeader> headers = new Dictionary<string, SettingsHeader>();
        readonly Dictionary<string, SettingRow> rows = new Dictionary<string, SettingRow>();
        readonly Dictionary<string, Control> cards = new Dictionary<string, Control>();
        readonly List<Control> results = new List<Control>();
        string current;
        SettingRow lit;
        Control pendingScroll;
        bool quiet;
        SettingRow logViceRow, pxeRow, accountRow, checkRow;

        public SettingsDialog(Theme t, SiteInfo info) : this(t, info, null)
        {
        }

        public SettingsDialog(Theme t, SiteInfo info, string page) : base("PxeVice settings", t)
        {
            this.info = info;
            subjects = new SubjectList(SettingsCatalog.Pages, PageGlyphs);
            subjects.Dock = DockStyle.Left;
            subjects.Width = Dpi.S(208);
            subjects.ApplyTheme(t);
            subjects.Picked += Safe.Wrap(delegate
            {
                quiet = true;
                search.Text = "";
                quiet = false;
                ShowPage(subjects.Selected, null);
            });
            head.Dock = DockStyle.Top;
            head.Height = Dpi.S(52);
            search.BorderStyle = BorderStyle.FixedSingle;
            search.Font = Fonts.Ui;
            head.Paint += delegate (object s, PaintEventArgs e)
            {
                Draw.Text(e.Graphics, SearchLabel, Fonts.Ui, new Rectangle(Dpi.S(16), 0, search.Left - Dpi.S(16), head.Height), theme.Text2, Draw.Line);
            };
            search.Location = new Point(Dpi.S(16) + Draw.Measure(SearchLabel, Fonts.Ui).Width + Dpi.S(12), Dpi.S(14));
            search.TextChanged += Safe.Wrap(delegate { if (!quiet) Searched(); });
            search.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && search.Text.Trim().Length > 0)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    List<SettingDef> hits = SettingsCatalog.Search(search.Text);
                    if (hits.Count > 0) Open(hits[0]);
                }
                else if (e.KeyCode == Keys.Down) { e.Handled = true; subjects.Focus(); }
            };
            head.Controls.Add(search);
            HandleCreated += delegate
            {
                try { SendMessage(search.Handle, 0x1501, (IntPtr)1, "Name or description (Ctrl+F)"); } catch (Exception) { }
            };
            Controls.Add(subjects);
            Controls.Add(head);
            Resize += delegate { Fit(); };
            PaintChrome(t);
            Fit();
            ActiveControl = search;
            string first = SettingsCatalog.Page(page) != null ? SettingsCatalog.Page(page).Id : SettingsCatalog.Remembered(Prefs.Get(SettingsCatalog.LastPagePref, null));
            ShowPage(first, null);
        }

        const string SearchLabel = "Search";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public string CurrentPage
        {
            get { return current; }
        }

        void PaintChrome(Theme t)
        {
            head.BackColor = t.Bg;
            head.Invalidate();
            search.BackColor = t.Field;
            search.ForeColor = t.Text;
            subjects.ApplyTheme(t);
        }

        void ApplyNewTheme(Theme t)
        {
            Retheme(t);
            PaintChrome(t);
            foreach (KeyValuePair<string, List<Control>> p in built)
                foreach (Control c in p.Value) { IThemed th = c as IThemed; if (th != null) th.ApplyTheme(t); }
            if (ThemeChanged != null) ThemeChanged(this, EventArgs.Empty);
        }

        // Narrow windows keep only the icons or first letters of the subjects.
        void Fit()
        {
            bool narrow = ClientSize.Width < Dpi.S(700);
            subjects.Compact = narrow;
            subjects.Width = narrow ? Dpi.S(60) : Dpi.S(208);
            search.Width = Math.Max(Dpi.S(120), Math.Min(Dpi.S(380), ClientSize.Width - search.Left - Dpi.S(16)));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (pendingScroll != null) { ScrollTo(pendingScroll); pendingScroll = null; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && search.Text.Length > 0)
            {
                search.Text = "";
                return true;
            }
            if (keyData == (Keys.Control | Keys.F))
            {
                search.Focus();
                search.SelectAll();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void ShowPage(string id, string highlight)
        {
            if (SettingsCatalog.Page(id) == null) id = SettingsCatalog.General;
            if (lit != null) { lit.Highlight = false; lit.Invalidate(); lit = null; }
            current = id;
            subjects.Selected = id;
            ClearResults();
            ClearItems();
            foreach (Control c in Built(id)) Add(c);
            RefreshMarks();
            Relayout();
            if (RememberPage) Prefs.Set(SettingsCatalog.LastPagePref, id);
            SettingRow row;
            Control card;
            if (highlight != null && rows.TryGetValue(highlight, out row) && cards.TryGetValue(highlight, out card))
            {
                row.Highlight = true;
                row.Invalidate();
                lit = row;
                if (Visible) ScrollTo(card);
                else pendingScroll = card;
            }
        }

        List<Control> Built(string id)
        {
            List<Control> l;
            if (built.TryGetValue(id, out l)) return l;
            SettingPage p = SettingsCatalog.Page(id);
            SettingsHeader h = new SettingsHeader(p.Title, p.Summary);
            string page = id;
            h.Reset.Click += Safe.Wrap(delegate { ResetPage(page); });
            headers[id] = h;
            l = new List<Control>();
            l.Add(h);
            switch (id)
            {
                case SettingsCatalog.General: BuildGeneral(l); break;
                case SettingsCatalog.Site: BuildSite(l); break;
                case SettingsCatalog.Account: BuildAccount(l); break;
                case SettingsCatalog.Watch: BuildWatch(l); break;
                case SettingsCatalog.Resets: BuildReset(l); break;
                case SettingsCatalog.Updates: BuildUpdates(l); break;
                case SettingsCatalog.LogVice: BuildLogVice(l); break;
                case SettingsCatalog.Data: BuildData(l); break;
                case SettingsCatalog.Needs: BuildNeeds(l); break;
            }
            // Rows that other parts of PxeVice make are matched by their title.
            foreach (Control c in l)
            {
                SettingRow r = RowOf(c);
                if (r == null || rows.ContainsValue(r)) continue;
                foreach (SettingDef d in SettingsCatalog.On(id))
                {
                    if ((d.Row ?? d.Title) != r.Title || rows.ContainsKey(d.Id)) continue;
                    rows[d.Id] = r;
                    cards[d.Id] = c;
                    r.WrapAccessory = true;
                    r.MoreToggled += delegate { Relayout(); };
                    break;
                }
            }
            built[id] = l;
            return l;
        }

        static SettingRow RowOf(Control c)
        {
            SettingRow r = c as SettingRow;
            if (r != null) return r;
            foreach (Control k in c.Controls) { r = k as SettingRow; if (r != null) return r; }
            return null;
        }

        // Registers the row of a setting, with its description from the catalog when the row has none.
        SettingRow Bind(string id, SettingRow row)
        {
            SettingDef d = SettingsCatalog.Find(id);
            if (row.Desc == null) row.Desc = d.Summary;
            if (row.More == null) row.More = d.More;
            row.WrapAccessory = true;
            rows[id] = row;
            cards[id] = row;
            row.MoreToggled += delegate { Relayout(); };
            Watch(row.Accessory);
            return row;
        }

        // The changed marks follow every change of a toggle, a choice or a field.
        void Watch(Control c)
        {
            Toggle tg = c as Toggle;
            if (tg != null) { tg.Changed += delegate { RefreshMarks(); }; return; }
            Segmented sg = c as Segmented;
            if (sg != null) { sg.Changed += delegate { RefreshMarks(); }; return; }
            ThemePicker tp = c as ThemePicker;
            if (tp != null) { tp.Changed += delegate { RefreshMarks(); }; return; }
            TextBox tb = c as TextBox;
            if (tb != null) tb.TextChanged += delegate { RefreshMarks(); };
        }

        void RefreshMarks()
        {
            foreach (SettingDef d in SettingsCatalog.All)
            {
                SettingRow r;
                if (!rows.TryGetValue(d.Id, out r)) continue;
                bool m = d.Changed(store);
                if (m != r.Marked) { r.Marked = m; r.Invalidate(); }
            }
            List<string> pages = new List<string>();
            foreach (SettingPage p in SettingsCatalog.Pages) if (SettingsCatalog.ChangedOn(p.Id, store).Count > 0) pages.Add(p.Id);
            subjects.SetChanged(pages);
            foreach (KeyValuePair<string, SettingsHeader> kv in headers) UpdateHeader(kv.Key, kv.Value);
        }

        void UpdateHeader(string page, SettingsHeader h)
        {
            bool can = SettingsCatalog.Resettable(page);
            int n = SettingsCatalog.ChangedOn(page, store).Count;
            h.Reset.Visible = can;
            h.Reset.Enabled = n > 0;
            h.NoteMark = n > 0;
            if (!can) h.Note = page == SettingsCatalog.Data ? null : "Nothing here is reset. Saved sites have their own Remove buttons.";
            else if (n > 0) h.Note = "Changed from the default. Reset puts back the defaults of this page.";
            else h.Note = "Every setting here has its default.";
            h.Invalidate();
        }

        void ResetPage(string page)
        {
            List<SettingDef> changed = SettingsCatalog.ChangedOn(page, store);
            if (changed.Count == 0) return;
            List<string> names = new List<string>();
            foreach (SettingDef d in changed) names.Add(d.Title);
            string what = string.Join(", ", names.ToArray());
            if (MessageBox.Show(this, "Put back the default of " + what + "? Your account and your saved sites are not changed.", "Reset " + SettingsCatalog.Page(page).Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            bool themeChanged = changed.Contains(SettingsCatalog.Find("theme"));
            SettingsCatalog.Reset(page, store);
            // The page is made again, so its controls show the defaults.
            List<Control> old;
            if (built.TryGetValue(page, out old))
            {
                built.Remove(page);
                headers.Remove(page);
                foreach (SettingDef d in SettingsCatalog.On(page)) { rows.Remove(d.Id); cards.Remove(d.Id); }
            }
            if (page == SettingsCatalog.Site) SiteChanged = true;
            ShowPage(page, null);
            if (themeChanged) ApplyNewTheme(Theme.Named(Theme.SavedName()));
            if (old != null) foreach (Control c in old) c.Dispose();
        }

        // ---------- search ----------

        void Searched()
        {
            string text = search.Text.Trim();
            if (text.Length == 0) { ShowPage(current, null); return; }
            if (lit != null) { lit.Highlight = false; lit.Invalidate(); lit = null; }
            subjects.Selected = null;
            ClearResults();
            ClearItems();
            List<SettingDef> hits = SettingsCatalog.Search(text);
            SettingsHeader h = new SettingsHeader("Search", hits.Count == 0
                ? "No setting matches “" + text + "”."
                : hits.Count + (hits.Count == 1 ? " setting matches “" : " settings match “") + text + "”. Enter opens the first.");
            h.Reset.Visible = false;
            results.Add(h);
            foreach (SettingDef d in hits)
            {
                SettingDef one = d;
                SettingPage p = SettingsCatalog.Page(d.Page);
                FlatButton show = SmallButton("Show", delegate { Open(one); });
                SettingRow r = new SettingRow(PageGlyphs[Array.IndexOf(SettingsCatalog.Pages, p)], d.Title, p.Title + ": " + d.Summary, show);
                r.Marked = d.Changed(store);
                results.Add(r);
            }
            foreach (Control c in results) Add(c);
            Relayout();
        }

        void ClearResults()
        {
            if (results.Count == 0) return;
            ClearItems();
            foreach (Control c in results) c.Dispose();
            results.Clear();
        }

        void Open(SettingDef d)
        {
            quiet = true;
            search.Text = "";
            quiet = false;
            ShowPage(d.Page, d.Id);
        }


        // ---------- the pages ----------

        List<string> Found
        {
            get { return info == null ? null : info.FoundPxeServers; }
        }

        void BuildGeneral(List<Control> l)
        {
            ThemePicker segTheme = new ThemePicker(Palettes.Choices);
            segTheme.SelectedName = Theme.SavedName();
            segTheme.Changed += Safe.Wrap(delegate
            {
                Prefs.Set("Theme", segTheme.SelectedName);
                ApplyNewTheme(Theme.Named(segTheme.SelectedName));
            });
            l.Add(Bind("theme", new SettingRow(Glyphs2.Palette, "Theme", null, segTheme)));
            Toggle onStart = new Toggle();
            onStart.On = Prefs.GetBool("WatchOnStart", false);
            onStart.Changed += Safe.Wrap(delegate { Prefs.SetBool("WatchOnStart", onStart.On); });
            l.Add(Bind("watchonstart", new SettingRow(Glyphs.Watch, "Watch when PxeVice opens", null, onStart)));
        }

        void BuildSite(List<Control> l)
        {
            l.Add(Bind("siteserver", Field(Glyphs3.Server, "Site server", null, "SiteServer", info != null ? info.SiteServer : null)));
            l.Add(Bind("sqlserver", Field(Glyphs3.Server, "SQL Server", null, "SqlServer", info != null ? info.SqlServer : null)));
            l.Add(Bind("database", Field(Glyphs2.Code, "Site database", null, "Database", info != null ? info.Database : null)));
            Toggle find = new Toggle();
            find.On = Prefs.GetBool("FindPxeServers", true);
            find.Changed += Safe.Wrap(delegate
            {
                Prefs.SetBool("FindPxeServers", find.On);
                SiteChanged = true;
                if (pxeRow != null) { pxeRow.Title = find.On ? "More PXE distribution points" : "Only these PXE distribution points"; pxeRow.Desc = PxeDesc(find.On); }
                Relayout();
            });
            l.Add(Bind("findpxe", new SettingRow(Glyphs3.Network, "Find the PXE distribution points from the site", FoundText(), find)));
            pxeRow = Field(Glyphs3.Network, find.On ? "More PXE distribution points" : "Only these PXE distribution points", PxeDesc(find.On), "PxeServers", null);
            l.Add(Bind("pxeservers", pxeRow));
            pxeRow.Title = find.On ? "More PXE distribution points" : "Only these PXE distribution points";
            l.Add(Bind("pxelog", Field(Glyphs.Log, "SMSPXE.log", null, "PxeLogPath", null)));
            List<SavedSite> saved = Sites.All();
            if (saved.Count > 0)
            {
                l.Add(new SectionTitle("Saved sites"));
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
                    row = new SettingRow(Glyphs3.Server, site.Name, site.Describe() + (string.IsNullOrEmpty(site.PxeServers) ? "" : " \u00b7 PXE: " + site.PxeServers) + ". Switch with Site in the toolbar.", new ButtonStrip(bRemove));
                    l.Add(row);
                }
            }
        }

        void BuildAccount(List<Control> l)
        {
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
            l.Add(Bind("account", new SettingRow(Glyphs.Shield, "Account", null, account)));
            FlatButton bForget = SmallButton("Forget the password", delegate
            {
                Secret.Save(RunAs.PasswordPref, null);
                accountRow.Desc = AccountState();
                Relayout();
            });
            FlatButton bUse = SmallButton("Use it now", delegate
            {
                UseAccountNow = true;
                Close();
            });
            accountRow = new SettingRow(Glyphs3.Play, "Use the account", AccountState(), new ButtonStrip(bUse, bForget));
            l.Add(Bind("useaccount", accountRow));
        }

        void BuildWatch(List<Control> l)
        {
            string[] secs = { "5", "10", "30", "60" };
            Segmented segWatch = new Segmented(new string[] { "5 s", "10 s", "30 s", "60 s" });
            int wi = Array.IndexOf(secs, Prefs.GetInt("WatchSeconds", 10).ToString(System.Globalization.CultureInfo.InvariantCulture));
            segWatch.Selected = wi < 0 ? 1 : wi;
            segWatch.Changed += Safe.Wrap(delegate { Prefs.Set("WatchSeconds", secs[segWatch.Selected]); });
            Toggle afterReset = new Toggle();
            afterReset.On = Prefs.GetBool("WatchAfterReset", true);
            afterReset.Changed += Safe.Wrap(delegate { Prefs.SetBool("WatchAfterReset", afterReset.On); });
            string[] hours = { "1", "4", "24", "72" };
            Segmented segHours = new Segmented(new string[] { "1 h", "4 h", "24 h", "3 days" });
            int hi = Array.IndexOf(hours, Prefs.GetInt("RequestHours", 24).ToString(System.Globalization.CultureInfo.InvariantCulture));
            segHours.Selected = hi < 0 ? 2 : hi;
            segHours.Changed += Safe.Wrap(delegate { Prefs.Set("RequestHours", hours[segHours.Selected]); });
            l.Add(Bind("watchseconds", new SettingRow(Glyphs.Watch, "Read every", null, segWatch)));
            l.Add(Bind("watchafterreset", new SettingRow(Glyphs.Watch, "Watch after a reset", null, afterReset)));
            l.Add(Bind("requesthours", new SettingRow(Glyphs3.History, "PXE boots from the last", null, segHours)));
        }

        void BuildReset(List<Control> l)
        {
            Toggle unknown = new Toggle();
            unknown.On = Prefs.GetBool("DeleteUnknown", true);
            unknown.Changed += Safe.Wrap(delegate { Prefs.SetBool("DeleteUnknown", unknown.On); });
            Toggle restart = new Toggle();
            restart.On = Prefs.GetBool("RestartPxe", false);
            restart.Changed += Safe.Wrap(delegate { Prefs.SetBool("RestartPxe", restart.On); });
            l.Add(Bind("deleteunknown", new SettingRow(Glyphs3.Delete, "Delete Unknown device records", null, unknown)));
            l.Add(Bind("restartpxe", new SettingRow(Glyphs3.Network, "Restart the PXE service", null, restart)));
        }

        void BuildUpdates(List<Control> l)
        {
            Toggle checkUpdates = new Toggle();
            checkUpdates.On = Updater.CheckOn;
            checkUpdates.Changed += Safe.Wrap(delegate { Updater.CheckOn = checkUpdates.On; });
            Toggle installUpdates = new Toggle();
            installUpdates.On = Updater.InstallOn;
            installUpdates.Changed += Safe.Wrap(delegate { Updater.InstallOn = installUpdates.On; });
            FlatButton bNow = SmallButton("Check now", delegate
            {
                if (Updater.CheckBlocked != null) { checkRow.Desc = Updater.CheckBlocked; Relayout(); return; }
                checkRow.Desc = "Asking GitHub\u2026";
                Relayout();
                Updater.CheckAsync(delegate (UpdateInfo r)
                {
                    try { BeginInvoke((MethodInvoker)delegate { checkRow.Desc = UpdateText(r); Relayout(); }); }
                    catch (Exception) { }
                });
            });
            FlatButton bNews = SmallButton("Update", delegate
            {
                UpdateInfo r = Updater.Last;
                if (r == null || !r.Newer) { checkRow.Desc = "There is no newer version to show. Press Check now."; Relayout(); return; }
                using (UpdateDialog d = new UpdateDialog(theme, r)) d.ShowDialog(this);
            });
            l.Add(Bind("updatecheck", new SettingRow(Glyphs3.Network, "Check for updates", null, checkUpdates)));
            l.Add(Bind("updateinstall", new SettingRow(Glyphs3.Play, "Install updates from PxeVice", null, installUpdates)));
            checkRow = new SettingRow(Glyphs.Info, "Version", "Version " + Cli.Version + ". " + LastCheckText(), new ButtonStrip(bNow, bNews));
            l.Add(Bind("version", checkRow));
        }

        void BuildLogVice(List<Control> l)
        {
            FlatButton bBrowse = SmallButton("Browse", delegate { Browse(); });
            FlatButton bClear = SmallButton("Clear", delegate
            {
                Prefs.Set("LogVicePath", null);
                UpdateLogVice();
                RefreshMarks();
            });
            logViceRow = new SettingRow(Glyphs2.Folder, "LogVice", "", new ButtonStrip(bBrowse, bClear));
            l.Add(Bind("logvice", logViceRow));
            UpdateLogVice();
        }

        void BuildNeeds(List<Control> l)
        {
            l.Add(Bind("needsread", new SettingRow(Glyphs3.Server, "Reading the site", null, null)));
            l.Add(Bind("needsdb", new SettingRow(Glyphs2.Code, "The site database", null, null)));
            l.Add(Bind("needslog", new SettingRow(Glyphs.Log, "SMSPXE.log", null, null)));
            l.Add(Bind("needsreset", new SettingRow(Glyphs3.Delete, "Resetting", null, null)));
        }

        void BuildData(List<Control> l)
        {
            SettingRow store = new SettingRow(Glyphs2.Settings, "Where settings are stored", Prefs.Location, null);
            store.MonoDesc = true;
            l.Add(Bind("settingsfile", store));
            FlatButton bFolder = SmallButton("Open folder", delegate { MainForm.OpenPath(ActivityLog.Folder); });
            SettingRow log = new SettingRow(Glyphs3.History, "Activity log", ActivityLog.FilePath + "\nEvery change PxeVice makes, with who made it, in the CMTrace format.", new ButtonStrip(bFolder));
            log.MonoDesc = true;
            l.Add(Bind("activitylog", log));
        }

        // ---------- helpers ----------

        string FoundText()
        {
            string what = "Every distribution point with PXE turned on, from the site. PxeVice reads SMSPXE.log on all of them at the same time.";
            List<string> found = Found;
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

        static string LastCheckText()
        {
            DateTime? last = Updater.LastChecked;
            if (Updater.CheckBlocked != null) return Updater.CheckBlocked;
            if (Updater.Last != null) return UpdateText(Updater.Last);
            return last.HasValue ? "Last checked " + Txt.Local(last.Value) + "." : "Not checked yet.";
        }

        static string UpdateText(UpdateInfo r)
        {
            if (r.Error != null) return r.Error;
            if (r.Newer)
            {
                string why = Updater.WhyNotInstall(r, Updater.IsInstalled(Program.Location));
                return "PxeVice " + r.Latest + " is available. Press Update." + (why == null ? " It can be installed from there." : " Install is not offered: " + why);
            }
            return "PxeVice is up to date. Checked " + Txt.Local(r.CheckedUtc) + ".";
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
}
