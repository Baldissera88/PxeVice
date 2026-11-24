namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Windows.Forms;

    // The home page: what the last day looked like, what needs a look, and where to go next.
    public sealed class OverviewView : Panel, IThemed
    {
        Theme theme;
        readonly List<Control> items = new List<Control>();
        readonly Panel col = new Panel();

        public OverviewView()
        {
            AutoScroll = true;
            DoubleBuffered = true;
            col.Location = new Point(0, 0);
            Controls.Add(col);
            Resize += delegate { Relayout(); };
        }

        public void ApplyTheme(Theme t)
        {
            theme = t;
            BackColor = col.BackColor = t.Bg;
            foreach (Control c in items) { IThemed th = c as IThemed; if (th != null) th.ApplyTheme(t); }
            Invalidate(true);
        }

        void Add(Control c)
        {
            items.Add(c);
            col.Controls.Add(c);
            IThemed th = c as IThemed;
            if (th != null && theme != null) th.ApplyTheme(theme);
        }

        void Clear()
        {
            col.SuspendLayout();
            foreach (Control c in items) { col.Controls.Remove(c); c.Dispose(); }
            items.Clear();
            col.ResumeLayout(false);
        }

        public void Relayout()
        {
            int pad = Dpi.S(8);
            int w = Math.Max(Dpi.S(240), ClientSize.Width - pad * 2 - SystemInformation.VerticalScrollBarWidth);
            int y = Dpi.S(4);
            col.SuspendLayout();
            foreach (Control c in items)
            {
                IMeasured m = c as IMeasured;
                int h = m != null ? m.Measure(w) : c.Height;
                if (c is SectionTitle && y > Dpi.S(8)) y += Dpi.S(10);
                c.SetBounds(0, y, w, h);
                y += h + (c is SectionTitle ? Dpi.S(2) : Dpi.S(6));
            }
            col.ResumeLayout(true);
            col.SetBounds(pad, AutoScrollPosition.Y, w, y + Dpi.S(16));
            AutoScrollMinSize = new Size(0, y + Dpi.S(16));
            foreach (Control c in items) c.Invalidate();
        }

        FlatButton Small(string text, Action click)
        {
            FlatButton b = new FlatButton(text, null);
            b.Compact = true;
            b.AutoWidth();
            b.Width += Dpi.S(8);
            b.Click += Safe.Wrap(delegate { click(); });
            return b;
        }

        // Shows the page again. showMachine opens a machine by its key.
        public void Fill(OverviewData o, List<RecentMachine> recent, bool connected, Action<string> showMachine, Action<string> showFilter, Action health, Action settings, Action help)
        {
            Clear();
            if (!connected)
            {
                Add(new SettingRow(Glyphs3.Server, "Connect to a site", "PxeVice has not read a site yet. Check the site server under Site in Settings, or press Refresh.", new ButtonStrip(Small("Settings", settings))));
            }
            else
            {
                SettingRow head = new SettingRow(o.NeedAttention == 0 ? Glyphs3.Accept : Glyphs3.Warning, o.Headline, o.Lead, null);
                if (o.NotServiced > 0) { head.Danger = true; head.RiskTag = o.NotServiced + " not serviced"; }
                Add(head);

                if (o.Attention.Count > 0)
                {
                    Add(new SectionTitle("Needs a look"));
                    int shown = 0;
                    foreach (Machine m in o.Attention)
                    {
                        if (++shown > 6) break;
                        Machine one = m;
                        string when = m.LastSeenUtc.HasValue ? Txt.Ago(m.LastSeenUtc, DateTime.UtcNow) : "";
                        SettingRow r = new SettingRow(Glyphs2.Device, m.Name, m.Found + (when.Length > 0 ? " · " + when : "") + (m.MacAddress != null ? " · " + m.MacAddress : ""), new ButtonStrip(Small("Open", delegate { showMachine(one.Key); })));
                        if (m.NotServiced) { r.Danger = true; r.RiskTag = "Not serviced"; }
                        Add(r);
                    }
                    if (o.Attention.Count > 6)
                        Add(new SettingRow(Glyphs2.Folder, "All " + o.Attention.Count + " machines", "The other " + (o.Attention.Count - 6) + " are on the Machines tab.", new ButtonStrip(Small("Show all", delegate { showFilter(null); }))));
                }

                if (o.Reasons.Count > 0)
                {
                    Add(new SectionTitle("What is left behind"));
                    int n = 0;
                    foreach (KeyValuePair<string, int> k in o.Reasons)
                    {
                        if (++n > 5) break;
                        string key = k.Key;
                        Add(new SettingRow(Glyphs3.History, k.Key, k.Value + (k.Value == 1 ? " machine" : " machines"), new ButtonStrip(Small("Show", delegate { showFilter(key); }))));
                    }
                }
            }

            if (recent.Count > 0)
            {
                Add(new SectionTitle("Opened lately"));
                foreach (RecentMachine r in recent)
                {
                    string key = r.Key;
                    Add(new SettingRow(Glyphs3.History, r.Name, "Open it again.", new ButtonStrip(Small("Open", delegate { showMachine(key); }))));
                }
            }

            Add(new SectionTitle("Start here"));
            Add(new SettingRow(Glyphs3.Network, "PXE health of the site", "Points, services, unknown computer support, the deployment to All Unknown Computers and the boot image. A single machine cannot boot while one of them is wrong.", new ButtonStrip(Small("Check", health))));
            Add(new SettingRow(Glyphs2.Help, "Why does a machine not PXE boot?", "Select it on the Machines tab. PxeVice says in plain words what decides it, and what to do.", new ButtonStrip(Small("Help", help))));
            Relayout();
        }
    }
}

namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Windows.Forms;

    public sealed partial class MainForm
    {
        string overviewSignature = "";
        const int WM_APPCOMMAND = 0x0319, AppBack = 1, AppForward = 2;

        // The mouse's back and forward buttons arrive as app commands, whatever control has the focus.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_APPCOMMAND)
            {
                int cmd = (int)(((long)m.LParam >> 16) & 0xFFF);
                if (cmd == AppBack) { GoBack(); m.Result = (IntPtr)1; return; }
                if (cmd == AppForward) { GoForward(); m.Result = (IntPtr)1; return; }
            }
            base.WndProc(ref m);
        }

        string SelectedKey()
        {
            if (tabs.Selected != TabMachines) return null;
            List<Machine> sel = machines.SelectedItems;
            return sel != null && sel.Count == 1 ? sel[0].Key : null;
        }

        // Remembers where the user is, for Back and Forward.
        void Visited()
        {
            if (navigating) return;
            nav.Go(new NavEntry(tabs.Selected, SelectedKey()));
        }

        // A machine was opened: it is remembered on the Overview.
        void OpenedMachine(List<Machine> sel)
        {
            if (sel == null || sel.Count != 1 || demo && Program.Start.Shot != null) return;
            RecentStore.Push(new PrefsSettingStore(), sel[0].Key, sel[0].Name);
        }

        void Go(NavEntry e)
        {
            if (e == null) return;
            navigating = true;
            try
            {
                tabs.Selected = e.Tab;
                if (e.Key != null && data != null)
                    foreach (Machine m in data.Machines) if (m.Key == e.Key) { Reveal(m); break; }
            }
            finally { navigating = false; }
        }

        void GoBack() { Go(nav.Back()); }
        void GoForward() { Go(nav.Forward()); }

        void ShowMachineByKey(string key)
        {
            if (data == null) return;
            foreach (Machine m in data.Machines)
                if (m.Key == key) { tabs.Selected = TabMachines; Reveal(m); Visited(); return; }
            Status("That machine is not in the list now. Its PXE flags or records may have been cleared.");
        }

        void ShowFilterText(string text)
        {
            tabs.Selected = TabMachines;
            Filter(null);
            search.Text = text ?? "";
            searchTimer.Stop();
            BindMachines();
            Visited();
        }

        void RefreshOverview()
        {
            int hours = Math.Max(1, Prefs.GetInt("RequestHours", 24));
            OverviewData o = PxeOverview.From(data, DateTime.UtcNow, hours);
            List<RecentMachine> recent = RecentStore.Load(new PrefsSettingStore());
            // The page is only made again when it says something different, so it does not jump while the user scrolls.
            System.Text.StringBuilder sig = new System.Text.StringBuilder(o.Headline + "|" + o.Lead + "|" + (data != null));
            foreach (Machine m in o.Attention) sig.Append(m.Key).Append(m.Found).Append(';');
            foreach (System.Collections.Generic.KeyValuePair<string, int> k in o.Reasons) sig.Append(k.Key).Append(k.Value).Append(';');
            foreach (RecentMachine r in recent) sig.Append(r.Key).Append(r.Name).Append(';');
            if (sig.ToString() == overviewSignature) return;
            overviewSignature = sig.ToString();
            overview.Fill(o, recent, data != null, ShowMachineByKey, ShowFilterText, delegate { OpenHealth(); }, delegate { OpenSettings(); }, delegate { About(); });
        }
    }
}
