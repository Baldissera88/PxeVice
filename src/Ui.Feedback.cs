namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Windows.Forms;

    public sealed class FieldBox : Panel, IThemed, IMeasured
    {
        Theme theme = Theme.Light();
        readonly string title, desc;
        readonly int lines;
        public readonly TextBox Box = new TextBox();

        public FieldBox(string title, string desc, int lines)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            this.title = title;
            this.desc = desc;
            this.lines = Math.Max(1, lines);
            Box.Multiline = this.lines > 1;
            Box.AcceptsReturn = this.lines > 1;
            Box.ScrollBars = this.lines > 1 ? ScrollBars.Vertical : ScrollBars.None;
            Box.BorderStyle = BorderStyle.FixedSingle;
            Box.Font = Fonts.Ui;
            Controls.Add(Box);
        }

        public string Value
        {
            get { return Box.Text.Trim(); }
        }

        int TextW(int width)
        {
            return Math.Max(Dpi.S(80), width - Dpi.S(40));
        }

        int BoxTop(int width)
        {
            int y = Dpi.S(12) + Fonts.Ui.Height;
            if (!string.IsNullOrEmpty(desc)) y += Dpi.S(2) + Draw.WrapHeight(desc, Fonts.Small, TextW(width));
            return y + Dpi.S(8);
        }

        int BoxH
        {
            get { return lines > 1 ? Fonts.Ui.Height * lines + Dpi.S(8) : Fonts.Ui.Height + Dpi.S(8); }
        }

        public int Measure(int width)
        {
            return BoxTop(width) + BoxH + Dpi.S(14);
        }

        public void ApplyTheme(Theme t)
        {
            theme = t;
            BackColor = t.Card;
            Box.BackColor = t.Field;
            Box.ForeColor = t.Text;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            Box.SetBounds(Dpi.S(20), BoxTop(Width), TextW(Width), BoxH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : theme.Bg);
            Draw.Card(g, ClientRectangle, theme);
            int y = Dpi.S(12);
            Draw.Text(g, title, Fonts.Ui, new Rectangle(Dpi.S(20), y, TextW(Width), Fonts.Ui.Height), theme.Text, Draw.Line);
            y += Fonts.Ui.Height + Dpi.S(2);
            if (!string.IsNullOrEmpty(desc))
            {
                int dh = Draw.WrapHeight(desc, Fonts.Small, TextW(Width));
                Draw.Text(g, desc, Fonts.Small, new Rectangle(Dpi.S(20), y, TextW(Width), dh + 2), theme.Text2, Draw.Wrap);
            }
        }
    }

    // A card that lists fields and their values: what goes along with a report.
    public sealed class FieldList : FlatControl, IMeasured
    {
        List<FeedbackField> rows = new List<FeedbackField>();

        public FieldList()
        {
            TabStop = false;
            SetStyle(ControlStyles.Selectable, false);
        }

        public void SetRows(List<FeedbackField> list)
        {
            rows = list ?? new List<FeedbackField>();
            Invalidate();
        }

        int LabelW(int width)
        {
            int w = 0;
            foreach (FeedbackField f in rows) w = Math.Max(w, Draw.Measure(f.Label, Fonts.SmallSemibold).Width + Dpi.S(20));
            return Math.Min(w, width / 3);
        }

        static Font ValueFont(FeedbackField f)
        {
            return f.Id == "errors" ? Fonts.MonoSmall : Fonts.Ui;
        }

        int RowH(FeedbackField f, int width)
        {
            int vw = Math.Max(Dpi.S(60), width - Dpi.S(40) - LabelW(width));
            return Math.Max(Fonts.Ui.Height, Draw.WrapHeight(f.Value.Length > 0 ? f.Value : "-", ValueFont(f), vw)) + Dpi.S(10);
        }

        public int Measure(int width)
        {
            int h = Dpi.S(16);
            foreach (FeedbackField f in rows) h += RowH(f, width);
            return h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(T.Bg);
            Draw.Card(g, ClientRectangle, T);
            int lw = LabelW(Width);
            int vx = Dpi.S(20) + lw;
            int vw = Math.Max(Dpi.S(60), Width - vx - Dpi.S(20));
            int y = Dpi.S(8);
            foreach (FeedbackField f in rows)
            {
                int h = RowH(f, Width);
                Draw.Text(g, f.Label, Fonts.SmallSemibold, new Rectangle(Dpi.S(20), y + Dpi.S(5), lw - Dpi.S(8), Fonts.Ui.Height), T.Text2, Draw.Line);
                string v = f.Value.Length > 0 ? f.Value : "-";
                Draw.Text(g, v, ValueFont(f), new Rectangle(vx, y + Dpi.S(5), vw, h), T.Text, Draw.Wrap);
                y += h;
            }
        }
    }

    // The pages under Help that open a GitHub issue, a discussion or an e-mail, filled in with what is on the page.
    public abstract class FeedbackPage : PageForm
    {
        readonly SettingRow result;

        protected FeedbackPage(string title, Theme t) : base(title, t)
        {
            Size = new Size(Dpi.S(800), Dpi.S(720));
            result = new SettingRow(Glyphs.Info, "Nothing has left this PC", "", null);
        }

        protected void AddResultRow()
        {
            Add(result);
        }

        protected void Said(string title, string text)
        {
            result.Title = title;
            result.Desc = text;
            result.Invalidate();
            Relayout();
        }

        // Docked from the right, after Close: the first one given ends up next to Close.
        protected FlatButton Footer(string text, bool primary, EventHandler click)
        {
            FlatButton b = new FlatButton(text, null);
            b.Primary = primary;
            b.AutoWidth();
            b.Width += Dpi.S(8);
            b.Dock = DockStyle.Right;
            b.Click += Safe.Wrap(click);
            b.ApplyTheme(theme);
            Panel gap = new Panel();
            gap.Width = Dpi.S(8);
            gap.Dock = DockStyle.Right;
            footer.Controls.Add(gap);
            footer.Controls.Add(b);
            gap.BringToFront();
            b.BringToFront();
            return b;
        }

        protected void Secondary()
        {
            bClose.Primary = false;
            bClose.ApplyTheme(theme);
        }

        // Opens the link in the browser or mail program. When the text was cut to fit, the whole text goes on the clipboard.
        protected void OpenLink(string url, string where, string fullText)
        {
            bool cut = url.IndexOf(Feedback.Escape(Feedback.CutNote), StringComparison.Ordinal) >= 0;
            try { Process.Start(url); }
            catch (Exception)
            {
                bool copied = Menus.Copy(fullText);
                Said("Could not open " + where, copied ? "The text is on the clipboard. Paste it into an e-mail to " + Feedback.Email + " or a new issue on " + Feedback.RepoUrl + "." : "Write to " + Feedback.Email + ".");
                return;
            }
            if (cut && Menus.Copy(fullText)) Said("Opened " + where, "Some text was too long for the link and was cut. The whole text is on the clipboard: paste the rest in yourself. You send it; PxeVice sent nothing.");
            else Said("Opened " + where, "Look it through and send it there. PxeVice sent nothing.");
        }

        // Opens an e-mail when Windows has a program for it. Without one, the text is copied and the page says so.
        protected void OpenMail(string url, string fullText)
        {
            try { Process.Start(url); }
            catch (Exception)
            {
                bool copied = Menus.Copy(fullText);
                Said("There is no e-mail program on this PC", copied ? "The text is on the clipboard. Write to " + Feedback.Email + " and paste it." : "Write to " + Feedback.Email + ".");
                return;
            }
            bool cut = url.IndexOf(Feedback.Escape(Feedback.CutNote), StringComparison.Ordinal) >= 0;
            if (cut && Menus.Copy(fullText)) Said("Opened an e-mail to " + Feedback.Email, "Some text was too long for the link and was cut. The whole text is on the clipboard: paste the rest in yourself. You send it; PxeVice sent nothing.");
            else Said("Opened an e-mail to " + Feedback.Email, "Look it through and send it there. PxeVice sent nothing.");
        }

        protected void OpenDiscussion(string text)
        {
            bool copied = Menus.Copy(text);
            try { Process.Start(Feedback.Discussions + "/new/choose"); }
            catch (Exception) { Said("Could not open GitHub Discussions", Feedback.Discussions); return; }
            Said("Opened GitHub Discussions", copied ? "The text is on the clipboard. Pick a category and paste it." : "Pick a category and write there.");
        }
    }

    // Help > Report a problem.
    public sealed class ProblemDialog : FeedbackPage
    {
        readonly FieldBox fWhat, fExpected, fSteps;
        readonly ProblemReport filled;

        public ProblemDialog(Theme t, ProblemReport report) : base("Report a problem", t)
        {
            filled = report;
            Add(new SettingRow(Glyphs.Shield, "PxeVice sends nothing by itself", "The buttons open a GitHub issue in your browser, or an e-mail in your mail program, filled in with what is on this page. You look it through and send it. PxeVice adds no server, site, machine or user names, MAC addresses or links: in its errors they are replaced with <name>, <mac>, <id> and <link>. They only go along if you type them in.", null));
            Add(new SectionTitle("In your words"));
            fWhat = new FieldBox("What happened", "What PxeVice did, or the message it showed.", 4);
            fExpected = new FieldBox("What you expected", null, 3);
            fSteps = new FieldBox("Steps to repeat it", "For example: select the machine, press Reset, turn on Delete the device.", 4);
            Add(fWhat);
            Add(fExpected);
            Add(fSteps);
            Add(new SectionTitle("Filled in by PxeVice, and sent along"));
            FieldList list = new FieldList();
            list.SetRows(report.Filled());
            Add(list);
            AddResultRow();
            Secondary();
            Footer("Open an e-mail", false, delegate { ProblemReport r = Report(); OpenMail(r.MailUrl(), Feedback.Body(r.Fields())); });
            Footer("Open a GitHub issue", true, delegate { ProblemReport r = Report(); OpenLink(r.IssueUrl(), "a new issue on GitHub", Feedback.Body(r.Fields())); });
        }

        public void Fill(string what, string expected, string steps)
        {
            fWhat.Box.Text = what ?? "";
            fExpected.Box.Text = expected ?? "";
            fSteps.Box.Text = steps ?? "";
        }

        ProblemReport Report()
        {
            ProblemReport r = ProblemReport.Make(filled.Version, filled.Windows, filled.Mode, null, null);
            r.Errors.AddRange(filled.Errors);
            r.What = fWhat.Value;
            r.Expected = fExpected.Value;
            r.Steps = fSteps.Value;
            return r;
        }
    }

    // Help > Suggest a feature.
    public sealed class FeatureDialog : FeedbackPage
    {
        readonly FieldBox fWhat, fWhy, fHow;

        public FeatureDialog(Theme t) : base("Suggest a feature", t)
        {
            Add(new SettingRow(Glyphs.Info, "A wish for PxeVice", "Say what you would like, why, and how you would use it. The buttons open a GitHub issue, a GitHub discussion or an e-mail with this text. You send it; PxeVice sends nothing by itself and adds nothing.", null));
            fWhat = new FieldBox("What you would like", null, 4);
            fWhy = new FieldBox("Why", "What it would solve, or what takes too long today.", 3);
            fHow = new FieldBox("How you would use it", null, 3);
            Add(fWhat);
            Add(fWhy);
            Add(fHow);
            AddResultRow();
            Secondary();
            Footer("Open an e-mail", false, delegate { FeatureWish w = Wish(); OpenMail(w.MailUrl(), Feedback.Body(w.Fields())); });
            Footer("Open a discussion", false, delegate { OpenDiscussion(Feedback.Body(Wish().Fields())); });
            Footer("Open a GitHub issue", true, delegate { FeatureWish w = Wish(); OpenLink(w.IssueUrl(), "a new issue on GitHub", Feedback.Body(w.Fields())); });
        }

        public void Fill(string what, string why, string how)
        {
            fWhat.Box.Text = what ?? "";
            fWhy.Box.Text = why ?? "";
            fHow.Box.Text = how ?? "";
        }

        FeatureWish Wish()
        {
            FeatureWish w = new FeatureWish();
            w.What = fWhat.Value;
            w.Why = fWhy.Value;
            w.How = fHow.Value;
            return w;
        }
    }

    // Help > Tell the author you use PxeVice.
    public sealed class TellAuthorDialog : FeedbackPage
    {
        readonly FieldBox fName, fCompany, fEmail, fUse;

        public TellAuthorDialog(Theme t) : base("Tell the author you use PxeVice", t)
        {
            Add(new SettingRow(Glyphs.Info, "Using PxeVice? Tell me", "PxeVice counts nothing and sends nothing by itself, so this is the only way the author knows. Every field is optional. The buttons open an e-mail to " + Feedback.Email + " or a GitHub discussion with this text, and you send it.", null));
            fName = new FieldBox("Name", null, 1);
            fCompany = new FieldBox("Company", null, 1);
            fEmail = new FieldBox("E-mail", "Only if you would like an answer.", 1);
            fUse = new FieldBox("What you use it for", "For example: support desk, re-imaging a lab, one ConfigMgr site.", 3);
            Add(fName);
            Add(fCompany);
            Add(fEmail);
            Add(fUse);
            AddResultRow();
            Secondary();
            Footer("Open a discussion", false, delegate { OpenDiscussion(Note().Text()); });
            Footer("Open an e-mail", true, delegate { UsageNote n = Note(); OpenMail(n.MailUrl(), n.Text()); });
        }

        UsageNote Note()
        {
            UsageNote n = new UsageNote();
            n.Name = fName.Value;
            n.Company = fCompany.Value;
            n.Email = fEmail.Value;
            n.Use = fUse.Value;
            n.Version = Cli.Version;
            return n;
        }
    }
}
