namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Windows.Forms;

    public sealed partial class MainForm
    {
        FlatButton bMore;
        FlatButton[] headerButtons;
        // The buttons that fold into More, the first to go first. Refresh, Reset, Settings and Help stay.
        FlatButton[] foldOrder;
        bool fitting;

        // The buttons that do not fit in the header go into a More menu, so the title is never covered.
        void BuildHeaderFold()
        {
            bMore = Button("More", null, delegate { MoreMenu().Show(bMore, new Point(bMore.Width, bMore.Height), ToolStripDropDownDirection.BelowLeft); });
            bMore.Visible = false;
            bMore.Margin = new Padding(Dpi.S(2), 0, 0, 0);
            Tip(bMore, "More: the buttons that do not fit in the window");
            toolbar.Controls.Add(bMore);
            toolbar.Controls.SetChildIndex(bMore, toolbar.Controls.GetChildIndex(bSettings));
            headerButtons = new FlatButton[] { bRefresh, bWatch, bReset, bExport, bPxeLog, bSite, bTheme, bMore, bSettings, bHelp };
            foldOrder = new FlatButton[] { bPxeLog, bExport, bTheme, bSite, bWatch };
            header.Layout += delegate { FitHeader(); };
            foreach (FlatButton b in headerButtons) b.SizeChanged += delegate { FitHeader(); };
            Shown += delegate { FitHeader(); };
            subtitle.AutoSize = false;
            subtitle.AutoEllipsis = true;
            subtitle.Height = Fonts.Small.Height + Dpi.S(4);
            FitHeader();
        }

        static int Taken(FlatButton b)
        {
            return b.Width + b.Margin.Horizontal;
        }

        void FitHeader()
        {
            if (fitting || bMore == null || header.ClientSize.Width <= 0) return;
            fitting = true;
            try
            {
                int titleW = TextRenderer.MeasureText(title.Text, title.Font).Width;
                int room = header.ClientSize.Width - title.Left - titleW - Dpi.S(28) - toolbar.Padding.Horizontal;
                List<FlatButton> shown = new List<FlatButton>();
                foreach (FlatButton b in headerButtons) if (b != bMore) shown.Add(b);
                int[] widths = new int[shown.Count];
                for (int i = 0; i < widths.Length; i++) widths[i] = Taken(shown[i]);
                int[] order = new int[foldOrder.Length];
                for (int i = 0; i < order.Length; i++) order[i] = shown.IndexOf(foldOrder[i]);
                bool[] hidden = NarrowFit.Hidden(widths, order, Taken(bMore), room);
                bool any = false;
                foreach (bool h in hidden) any |= h;
                toolbar.SuspendLayout();
                bMore.Visible = any;
                for (int i = 0; i < shown.Count; i++) shown[i].Visible = !hidden[i];
                toolbar.ResumeLayout(true);
                // A folded button keeps the place of More, so the menu it opens drops from there.
                foreach (FlatButton b in shown) if (!b.Visible) b.Location = bMore.Location;
                subtitle.Width = Math.Max(Dpi.S(40), header.ClientSize.Width - subtitle.Left - Dpi.S(16));
            }
            finally { fitting = false; }
        }

        ContextMenuStrip MoreMenu()
        {
            ContextMenuStrip m = Menus.New(theme);
            foreach (FlatButton b in foldOrder)
            {
                if (b.Visible) continue;
                FlatButton button = b;
                string text = string.IsNullOrEmpty(b.Text) ? tips.GetToolTip(b) : b.Text;
                Menus.Add(m, text, delegate { button.PerformClick(); }).Checked = b.Checked;
            }
            return m;
        }
    }
}
