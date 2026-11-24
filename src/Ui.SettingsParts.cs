namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;

    public sealed class PrefsSettingStore : ISettingStore
    {
        public string Get(string name) { return Prefs.Get(name, null); }
        public void Set(string name, string value) { Prefs.Set(name, value); }
    }

    // The subjects on the left of Settings. When the window is narrow, only their icons or first letters show.
    public sealed class SubjectList : FlatControl
    {
        readonly SettingPage[] pages;
        readonly string[] glyphs;
        readonly HashSet<string> changed = new HashSet<string>();
        readonly ToolTip tip = new ToolTip();
        string selected;
        int hot = -1;
        bool compact;
        public event EventHandler Picked;

        public SubjectList(SettingPage[] pages, string[] glyphs)
        {
            this.pages = pages;
            this.glyphs = glyphs;
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public string Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        public bool Compact
        {
            get { return compact; }
            set { if (compact == value) return; compact = value; Invalidate(); }
        }

        public void SetChanged(ICollection<string> ids)
        {
            changed.Clear();
            foreach (string id in ids) changed.Add(id);
            Invalidate();
        }

        int RowH
        {
            get { return Dpi.S(38); }
        }

        Rectangle RowRect(int i)
        {
            return new Rectangle(Dpi.S(8), Dpi.S(6) + i * RowH, Math.Max(0, Width - Dpi.S(17)), RowH - Dpi.S(4));
        }

        int IndexAt(Point p)
        {
            for (int i = 0; i < pages.Length; i++) if (RowRect(i).Contains(p)) return i;
            return -1;
        }

        int SelectedIndex
        {
            get
            {
                for (int i = 0; i < pages.Length; i++) if (pages[i].Id == selected) return i;
                return -1;
            }
        }

        void Pick(int i)
        {
            if (i < 0 || i >= pages.Length) return;
            selected = pages[i].Id;
            Invalidate();
            if (Picked != null) Picked(this, EventArgs.Empty);
        }

        static string Initials(string title)
        {
            string t = title.Replace("/", " ").Trim();
            string[] w = t.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (w.Length >= 2 && char.IsUpper(w[1][0])) return w[0].Substring(0, 1) + w[1].Substring(0, 1);
            return t.Length >= 2 ? t.Substring(0, 2) : t;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(T.Bg);
            using (Pen p = new Pen(T.Divider)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);
            bool icons = Fonts.Icons != null;
            for (int i = 0; i < pages.Length; i++)
            {
                Rectangle r = RowRect(i);
                bool sel = pages[i].Id == selected;
                if (sel)
                {
                    Shapes.Fill(g, r, Dpi.S(5), Theme.Mix(T.Bg, T.Accent, T.IsDark ? 0.22f : 0.13f));
                    Shapes.Fill(g, new RectangleF(r.X, r.Y + Dpi.S(9), Dpi.S(3), r.Height - Dpi.S(18)), 1.5f, T.Accent);
                }
                else if (i == hot) Shapes.Fill(g, r, Dpi.S(5), T.Hover);
                Color fg = sel ? T.Text : T.Text2;
                Font f = sel ? Fonts.UiSemibold : Fonts.Ui;
                if (compact)
                {
                    if (icons) Draw.Glyph(g, glyphs[i], r, sel ? T.Accent : fg);
                    else Draw.Text(g, Initials(pages[i].Title), f, r, sel ? T.Accent : fg, Draw.Line | TextFormatFlags.HorizontalCenter);
                }
                else
                {
                    int x = r.X + Dpi.S(14);
                    if (icons)
                    {
                        Draw.Glyph(g, glyphs[i], new Rectangle(x - Dpi.S(2), r.Y, Dpi.S(20), r.Height), sel ? T.Accent : fg);
                        x += Dpi.S(28);
                    }
                    Draw.Text(g, pages[i].Title, f, new Rectangle(x, r.Y, r.Right - x - Dpi.S(20), r.Height), fg, Draw.Line);
                }
                if (changed.Contains(pages[i].Id))
                {
                    int d = Dpi.S(6);
                    float cx = compact ? r.Right - Dpi.S(8) - d : r.Right - Dpi.S(10) - d;
                    float cy = compact ? r.Y + Dpi.S(6) : r.Y + (r.Height - d) / 2f;
                    Shapes.Fill(g, new RectangleF(cx, cy, d, d), d / 2f, T.Accent);
                }
            }
            int si = SelectedIndex;
            if (Focused && ShowFocusCues && si >= 0) Shapes.Stroke(g, RowRect(si), Dpi.S(5), T.Text, 2f);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
            if (i != hot)
            {
                hot = i;
                Invalidate();
                tip.SetToolTip(this, compact && i >= 0 ? pages[i].Title : null);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hot = -1;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            Pick(IndexAt(e.Location));
            base.OnMouseClick(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Up || k == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int si = SelectedIndex;
            if (e.KeyCode == Keys.Up) { Pick(si <= 0 ? 0 : si - 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Pick(Math.Min(pages.Length - 1, si + 1)); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }

    // The top of a Settings page: its subject, what it holds, and Reset.
    sealed class SettingsHeader : Panel, IThemed, IMeasured
    {
        Theme theme = Theme.Light();
        public string Title, Summary, Note;
        // Draws the changed mark before the note, so the note explains it.
        public bool NoteMark;
        public readonly FlatButton Reset = new FlatButton("Reset", null);

        public SettingsHeader(string title, string summary)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Title = title;
            Summary = summary;
            Reset.Compact = true;
            Reset.AutoWidth();
            Reset.Width += Dpi.S(8);
            Controls.Add(Reset);
        }

        int TextW(int width)
        {
            return Math.Max(Dpi.S(80), width - (Reset.Visible ? Reset.Width + Dpi.S(16) : 0));
        }

        int MarkW
        {
            get { return NoteMark ? Dpi.S(14) : 0; }
        }

        public int Measure(int width)
        {
            int tw = TextW(width);
            int h = Dpi.S(8) + Fonts.Title.Height + Dpi.S(4) + Draw.WrapHeight(Summary, Fonts.Ui, tw);
            if (!string.IsNullOrEmpty(Note)) h += Dpi.S(6) + Draw.WrapHeight(Note, Fonts.Small, tw - MarkW);
            return h + Dpi.S(8);
        }

        public void ApplyTheme(Theme t)
        {
            theme = t;
            BackColor = t.Bg;
            Reset.ApplyTheme(t);
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            Reset.Location = new Point(Width - Reset.Width, Dpi.S(8));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(theme.Bg);
            int tw = TextW(Width);
            int y = Dpi.S(8);
            Draw.Text(g, Title, Fonts.Title, new Rectangle(Dpi.S(2), y, tw, Fonts.Title.Height + 2), theme.Text, Draw.Line);
            y += Fonts.Title.Height + Dpi.S(4);
            int sh = Draw.WrapHeight(Summary, Fonts.Ui, tw);
            Draw.Text(g, Summary, Fonts.Ui, new Rectangle(Dpi.S(2), y, tw, sh + 2), theme.Text2, Draw.Wrap);
            y += sh;
            if (string.IsNullOrEmpty(Note)) return;
            y += Dpi.S(6);
            if (NoteMark)
            {
                int d = Dpi.S(7);
                Shapes.Fill(g, new RectangleF(Dpi.S(3), y + (Fonts.Small.Height - d) / 2f, d, d), d / 2f, theme.Accent);
            }
            int nh = Draw.WrapHeight(Note, Fonts.Small, tw - MarkW);
            Draw.Text(g, Note, Fonts.Small, new Rectangle(Dpi.S(2) + MarkW, y, tw - MarkW, nh + 2), theme.Text3, Draw.Wrap);
        }
    }

    // Settings: a list of subjects on the left, one page on the right, and a search box over all of them.
}
