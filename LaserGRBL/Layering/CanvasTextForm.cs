using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using LaserGRBL.UserControls;

namespace LaserGRBL.Layering
{
    internal sealed class CanvasTextForm : Form
    {
        private readonly TextBox content = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AcceptsReturn = true };
        private readonly ComboBox fonts = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ComboBox styles = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ComboBox alignment = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly NumericUpDown size = new NumericUpDown { Minimum = .1m, Maximum = 1000, DecimalPlaces = 2, Value = 10, Dock = DockStyle.Fill };
        private readonly Label error = new Label { AutoSize = true, Dock = DockStyle.Fill };
        private readonly Panel preview = new Panel { Dock = DockStyle.Fill, MinimumSize = new Size(100, 80) };
        private VectorItem generated;
        public TextObject Value { get; private set; }

        private CanvasTextForm(TextObject initial)
        {
            Text = LayerText.Get("TextTool"); StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            Size = new Size(570, 600); MinimumSize = new Size(410, 440);
            AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 8 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            for (int i = 1; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            AddRow(layout, 0, "TextContent", content); AddRow(layout, 1, "TextFont", fonts);
            AddRow(layout, 2, "TextStyle", styles); AddRow(layout, 3, "TextSize", size); AddRow(layout, 4, "TextAlignment", alignment);
            layout.Controls.Add(error, 0, 5); layout.SetColumnSpan(error, 2);
            layout.Controls.Add(preview, 0, 6); layout.SetColumnSpan(preview, 2);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = LayerText.Get("Cancel"), AutoSize = true, MinimumSize = new Size(90, 32), DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = LayerText.Get("Apply"), AutoSize = true, MinimumSize = new Size(90, 32) };
            buttons.Controls.Add(cancel); buttons.Controls.Add(ok); layout.Controls.Add(buttons, 0, 7); layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout); CancelButton = cancel;
            foreach (var name in FontFamily.Families.Select(f => f.Name).Distinct().OrderBy(n => n)) fonts.Items.Add(name);
            alignment.Items.AddRange(new object[] { LayerText.Get("AlignLeft"), LayerText.Get("AlignCenter"), LayerText.Get("AlignRight") });
            initial = initial ?? new TextObject(); content.Text = initial.Content.Replace("\r\n", "\n").Replace("\n", "\r\n");
            size.Value = (decimal)Math.Max(.1, Math.Min(1000, initial.SizeMm)); alignment.SelectedIndex = (int)initial.Alignment;
            fonts.SelectedIndexChanged += (s, e) => { RefreshStyles(FontStyle.Regular); RefreshPreview(); };
            fonts.SelectedItem = initial.FontFamily; RefreshStyles(initial.Style);
            if (fonts.SelectedIndex < 0) error.Text = string.Format(LayerText.Get("MissingFont"), initial.FontFamily);
            content.TextChanged += (s, e) => RefreshPreview(); styles.SelectedIndexChanged += (s, e) => RefreshPreview();
            alignment.SelectedIndexChanged += (s, e) => RefreshPreview(); size.ValueChanged += (s, e) => RefreshPreview();
            preview.Paint += PaintPreview; preview.Resize += (s, e) => preview.Invalidate();
            ok.Click += (s, e) => { RefreshPreview(); if (generated != null) { Value = generated.Text.Clone(); DialogResult = DialogResult.OK; } };
            FadeTheme.Apply(this); FadeTheme.SetButtonRole(ok, FadeButtonRole.Primary); RefreshPreview();
        }
        private static void AddRow(TableLayoutPanel layout, int row, string key, Control control)
        {
            layout.Controls.Add(new Label { Text = LayerText.Get(key), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) }, 0, row);
            control.Margin = new Padding(0, 4, 0, 4); layout.Controls.Add(control, 1, row);
        }
        private sealed class StyleChoice
        {
            public FontStyle Value; public string Caption;
            public override string ToString() { return Caption; }
        }
        private void RefreshStyles(FontStyle preferred)
        {
            styles.Items.Clear(); if (fonts.SelectedItem == null) return;
            using (var family = new FontFamily((string)fonts.SelectedItem))
                foreach (var style in new[] { FontStyle.Regular, FontStyle.Bold, FontStyle.Italic, FontStyle.Bold | FontStyle.Italic })
                    if (family.IsStyleAvailable(style)) styles.Items.Add(new StyleChoice { Value = style, Caption = LayerText.Get("FontStyle" + (int)style) });
            styles.SelectedItem = styles.Items.Cast<StyleChoice>().FirstOrDefault(s => s.Value == preferred);
            if (styles.SelectedIndex < 0 && styles.Items.Count > 0) styles.SelectedIndex = 0;
        }
        private void RefreshPreview()
        {
            generated = null;
            try
            {
                if (fonts.SelectedItem == null || styles.SelectedItem == null) { preview.Invalidate(); return; }
                generated = CanvasObjects.Text(new TextObject { Content = content.Text, FontFamily = (string)fonts.SelectedItem, Style = ((StyleChoice)styles.SelectedItem).Value,
                    SizeMm = (double)size.Value, Alignment = (StringAlignment)Math.Max(0, alignment.SelectedIndex) }, PointF.Empty);
                error.Text = "";
            }
            catch (Exception ex) { error.Text = ex.Message; }
            preview.Invalidate();
        }
        private void PaintPreview(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(ColorScheme.PreviewBackColor); if (generated == null) return;
            var bounds = CanvasObjects.Bounds(generated.Paths); if (bounds.Width <= 0 || bounds.Height <= 0) return;
            float scale = Math.Min(Math.Max(1, preview.Width - 24) / bounds.Width, Math.Max(1, preview.Height - 24) / bounds.Height);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath(FillMode.Alternate))
            using (var brush = new SolidBrush(ColorScheme.FormForeColor))
            {
                foreach (var contour in generated.Paths)
                {
                    var points = contour.Points.Select(p => new PointF(12 + ((float)p.X - bounds.Left) * scale, preview.Height - 12 - ((float)p.Y - bounds.Top) * scale)).ToArray();
                    if (points.Length >= 3) path.AddPolygon(points);
                }
                e.Graphics.FillPath(brush, path);
            }
        }
        public static bool TryEdit(IWin32Window owner, TextObject initial, out TextObject value)
        {
            using (var form = new CanvasTextForm(initial))
            { bool accepted = form.ShowDialog(owner) == DialogResult.OK; value = accepted ? form.Value : initial; return accepted; }
        }
    }
}
