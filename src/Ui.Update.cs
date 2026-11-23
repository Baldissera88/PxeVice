namespace PxeVice
{
    using System;
    using System.Drawing;
    using System.Threading;
    using System.Windows.Forms;

    // The small window of the helper started with --apply-update, while PxeVice is replaced.
    public sealed class UpdateWindow : Form, IUpdateProgress
    {
        readonly Label status = new Label();
        readonly Label problem = new Label();
        readonly Button ok = new Button();
        readonly UpdateJob job;
        bool failed;

        public UpdateWindow(UpdateJob j)
        {
            job = j;
            Text = "Updating PxeVice";
            Icon = Program.AppIcon();
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Dpi.S(480), Dpi.S(150));
            status.SetBounds(Dpi.S(16), Dpi.S(16), Dpi.S(448), Dpi.S(40));
            problem.SetBounds(Dpi.S(16), Dpi.S(60), Dpi.S(448), Dpi.S(50));
            problem.ForeColor = Color.Firebrick;
            ok.Text = "Close";
            ok.SetBounds(Dpi.S(384), Dpi.S(114), Dpi.S(80), Dpi.S(28));
            ok.Visible = false;
            ok.Click += delegate { Close(); };
            Controls.Add(status);
            Controls.Add(problem);
            Controls.Add(ok);
            Shown += delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { job.Run(this); }
                    catch (Exception ex) { Problem(ex.Message); Finish(); }
                });
            };
        }

        void Ui(MethodInvoker m)
        {
            try { if (IsHandleCreated) BeginInvoke(m); }
            catch (Exception) { }
        }

        public void Status(string text)
        {
            Ui(delegate { status.Text = text; });
        }

        public void Problem(string text)
        {
            failed = true;
            Ui(delegate { problem.Text = (problem.Text.Length > 0 ? problem.Text + "\r\n" : "") + text; });
        }

        public void Finish()
        {
            Ui(delegate
            {
                if (!failed) Close();
                else
                {
                    status.Text = "The update did not finish.";
                    ok.Visible = true;
                }
            });
        }
    }

    // What is new, and the buttons to install it or open the release.
    public sealed class UpdateDialog : PageForm
    {
        readonly UpdateInfo info;
        readonly SettingRow state;
        readonly FlatButton bInstall;
        bool busy;

        public UpdateDialog(Theme t, UpdateInfo r) : base("PxeVice " + r.Latest + " is available", t)
        {
            info = r;
            Size = new Size(Dpi.S(700), Dpi.S(560));
            Add(new SettingRow(Glyphs.Info, "PxeVice " + r.Latest, "You have " + Cli.Version + ".", null));
            if (!string.IsNullOrEmpty(r.Notes)) Add(new SectionTitle("What is new"));
            if (!string.IsNullOrEmpty(r.Notes)) Add(new SettingRow(Glyphs.Log, "Release notes", r.Notes, null));

            bool installed = Updater.IsInstalled(Program.Location);
            string why = Updater.WhyNotInstall(r, installed);
            string how;
            if (why != null) how = why + " You can download the new version from the release page.";
            else how = installed
                ? "PxeVice downloads the MSI, checks it against the release's checksum, closes, installs it (Windows asks for administrator rights) and opens again."
                : "This copy is not installed with the MSI. PxeVice downloads PxeVice.exe, checks it against the release's checksum, closes, replaces itself and opens again.";
            state = new SettingRow(Glyphs3.Play, "Install", how, null);
            Add(state);

            bClose.Text = "Not now";
            bClose.AutoWidth();
            bClose.Width = Math.Max(bClose.Width, Dpi.S(96));
            bClose.Primary = false;
            bClose.ApplyTheme(t);
            FlatButton bPage = new FlatButton("Release page", null);
            bPage.Dock = DockStyle.Left;
            bPage.ApplyTheme(t);
            bPage.Click += Safe.Wrap(delegate { MainForm.OpenPath(info.Url ?? Updater.ReleasesUrl); });
            footer.Controls.Add(bPage);
            bInstall = new FlatButton("Install", Glyphs3.Play);
            bInstall.Primary = true;
            bInstall.Dock = DockStyle.Right;
            bInstall.Width = Math.Max(bInstall.Width, Dpi.S(110));
            bInstall.Visible = Updater.CanInstall(r, installed);
            bInstall.ApplyTheme(t);
            bInstall.Click += Safe.Wrap(delegate { Install(installed); });
            Panel gap = new Panel();
            gap.Dock = DockStyle.Right;
            gap.Width = Dpi.S(8);
            footer.Controls.Add(gap);
            footer.Controls.Add(bInstall);
            footer.Controls.SetChildIndex(bInstall, 0);
            footer.Controls.SetChildIndex(gap, 1);
        }

        void Install(bool installed)
        {
            if (busy) return;
            busy = true;
            bInstall.Enabled = false;
            state.Desc = "Downloading…";
            Relayout();
            UpdateInfo r = info;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string file = null, error = null;
                try
                {
                    file = Updater.Prepare(r, installed, delegate (int pct)
                    {
                        try { BeginInvoke((MethodInvoker)delegate { state.Desc = "Downloading… " + pct + " %"; Relayout(); }); } catch (Exception) { }
                    });
                    Updater.Launch(file, Program.Location);
                }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        busy = false;
                        if (error != null)
                        {
                            state.Desc = error;
                            bInstall.Enabled = true;
                            Relayout();
                            return;
                        }
                        // The helper waits for PxeVice to close.
                        Application.Exit();
                    });
                }
                catch (Exception) { }
            });
        }
    }
}
