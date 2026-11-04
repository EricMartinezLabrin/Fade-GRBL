using System;
using System.Drawing;
using System.Windows.Forms;
using LaserGRBL.Layering;
using LaserGRBL.UserControls;

namespace LaserGRBL
{
    public partial class MainForm
    {
        private FadeBrandHeader brandHeader;
        private ToolStripMenuItem fadeLightItem, fadeDarkItem;

        private void BuildDesignShell()
        {
            MnGenerate.Text = LayerText.Get("DesignGenerate");
            questionMarkToolStripMenuItem.Text = LayerText.Get("DesignHelp");
            fadeLightItem = new ToolStripMenuItem(LayerText.Get("DesignLight")) { Tag = ColorScheme.Scheme.FadeLight };
            fadeDarkItem = new ToolStripMenuItem(LayerText.Get("DesignDark")) { Tag = ColorScheme.Scheme.FadeDark };
            EventHandler selectTheme = (s, e) =>
            {
                ColorScheme.CurrentScheme = (ColorScheme.Scheme)((ToolStripMenuItem)s).Tag;
                Settings.SetObject("Fade Design Theme", ColorScheme.CurrentScheme);
                RefreshColorSchema();
            };
            fadeLightItem.Click += selectTheme; fadeDarkItem.Click += selectTheme;
            var owner = cadStyleToolStripMenuItem.GetCurrentParent();
            owner.Items.Insert(0, fadeLightItem); owner.Items.Insert(1, fadeDarkItem); owner.Items.Insert(2, new ToolStripSeparator());

            brandHeader = new FadeBrandHeader();
            var shell = new TableLayoutPanel { Name = "DesignShell", Dock = DockStyle.Fill, Size = ClientSize, ColumnCount = 1, RowCount = 5, Padding = new Padding(8), Margin = Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize)); shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Control progress = ConnectionForm.CreateDesignJobPanel();
            Controls.Clear();
            int row = 0;
            foreach (Control control in new Control[] { brandHeader, MMn, splitContainer1, progress, StatusBar })
            { control.Dock = DockStyle.Fill; control.Margin = Padding.Empty; shell.Controls.Add(control, 0, row++); }
            Controls.Add(shell);
            splitContainer1.BorderStyle = BorderStyle.None; splitContainer1.SplitterWidth = 8;
            splitContainer1.Panel1MinSize = 240; splitContainer1.Panel2MinSize = 260;
            splitContainer1.SplitterDistance = Math.Max(240, Math.Min(Settings.GetObject("MainForm Splitter Position", 280), Math.Max(240, splitContainer1.Width - 270)));
            JogForm.Font = FadeTheme.BodyFont;
            Core.OnFileLoaded += (elapsed, filename) =>
            {
                if (brandHeader.IsDisposed) return;
                if (brandHeader.InvokeRequired) brandHeader.BeginInvoke((MethodInvoker)(() => { if (!brandHeader.IsDisposed) brandHeader.SetProject(filename); }));
                else brandHeader.SetProject(filename);
            };
            Shown += (s, e) =>
            {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                if (Height > area.Height || Width > area.Width) Bounds = new Rectangle(area.Left + 12, area.Top + 12, Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
            };
        }

        private void RefreshDesignTheme()
        {
            if (fadeLightItem == null) return;
            fadeLightItem.Checked = ColorScheme.CurrentScheme == ColorScheme.Scheme.FadeLight;
            fadeDarkItem.Checked = ColorScheme.CurrentScheme == ColorScheme.Scheme.FadeDark;
            FadeTheme.Apply(this);
            ConnectionForm.RefreshDesignTheme();
            if (brandHeader != null) brandHeader.Invalidate();
        }
    }
}
