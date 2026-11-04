using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace LaserGRBL.UserControls
{
    public enum FadeButtonRole { Secondary, Primary, Danger }

    // Shared styling for existing WinForms screens; never changes values or click handlers.
    public static class FadeTheme
    {
        public static readonly Font BodyFont = new Font("Segoe UI", 10f);
        public static readonly Font CaptionFont = new Font("Segoe UI", 9f);
        public static readonly Font HeadingFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        public static readonly Font CodeFont = new Font("Consolas", 9f);
        private sealed class State { public FadeButtonRole Role; public bool Observed; public bool FormStyled; }
        private static readonly ConditionalWeakTable<Control, State> States = new ConditionalWeakTable<Control, State>();
        private static readonly ToolStripRenderer Renderer = new FadeRenderer();
        private static bool installed;

        public static void Install()
        {
            if (installed) return;
            installed = true; Application.Idle += StyleOpenForms;
        }

        private static void StyleOpenForms(object sender, EventArgs args)
        {
            if (!ColorScheme.IsFade) return;
            for (int i = 0; i < Application.OpenForms.Count; i++)
            {
                Form form = Application.OpenForms[i]; State state = States.GetOrCreateValue(form);
                if (state.FormStyled || form.IsDisposed) continue;
                state.FormStyled = true; Apply(form);
            }
        }

        public static void SetButtonRole(Button button, FadeButtonRole role)
        { States.GetOrCreateValue(button).Role = role; StyleButton(button); }

        public static void Apply(Control root)
        {
            if (!ColorScheme.IsFade || root == null || root.IsDisposed) return;
            root.SuspendLayout();
            try { ApplyTree(root); }
            finally { root.ResumeLayout(true); }
            root.Invalidate(true);
        }

        private static void ApplyTree(Control control)
        {
            if (control.Name == "LayerColorPreview" || control is PictureBox) return;
            if (control is GrblPanel || control is GrblPanel3D)
            { control.BackColor = ColorScheme.PreviewBackColor; return; }
            control.ForeColor = ColorScheme.FormForeColor;
            control.BackColor = ColorScheme.FormBackColor;
            if (control.Name == "DesignShell") control.BackColor = ColorScheme.AppBackground;
            State state = States.GetOrCreateValue(control);
            if (!state.Observed)
            {
                state.Observed = true;
                control.ControlAdded += (s, e) => { if (ColorScheme.IsFade) Apply(e.Control); };
            }
            Form form = control as Form;
            if (form != null)
            {
                if (form.AutoScaleMode == AutoScaleMode.Inherit) form.AutoScaleMode = AutoScaleMode.Font;
                form.Font = BodyFont;
                form.BackColor = ColorScheme.AppBackground;
                Button accept = form.AcceptButton as Button;
                if (accept != null) States.GetOrCreateValue(accept).Role = FadeButtonRole.Primary;
            }
            ToolStrip strip = control as ToolStrip;
            if (strip != null)
            {
                strip.Renderer = Renderer; strip.GripStyle = ToolStripGripStyle.Hidden; strip.Font = BodyFont;
                StyleItems(strip.Items);
            }
            Button button = control as Button;
            if (button != null)
            {
                if (button.DialogResult == DialogResult.OK && state.Role == FadeButtonRole.Secondary) state.Role = FadeButtonRole.Primary;
                StyleButton(button);
                if (button.Parent is TableLayoutPanel || button.Parent is FlowLayoutPanel)
                    button.MinimumSize = new Size(button.MinimumSize.Width, 32);
            }
            DataGridView grid = control as DataGridView;
            if (grid != null) StyleGrid(grid);
            ComboBox combo = control as ComboBox;
            if (combo != null) combo.FlatStyle = FlatStyle.Flat;
            LinkLabel link = control as LinkLabel;
            if (link != null) { link.LinkColor = ColorScheme.LinkColor; link.VisitedLinkColor = ColorScheme.LinkColor; }
            SplitContainer split = control as SplitContainer;
            if (split != null) { split.BackColor = ColorScheme.AppBackground; split.BorderStyle = BorderStyle.None; }
            ImageButton icon = control as ImageButton;
            if (icon != null) { icon.BorderStyle = BorderStyle.None; icon.RoundedBorders = false; if (string.IsNullOrEmpty(icon.AccessibleName)) icon.AccessibleName = icon.Name; }
            foreach (Control child in control.Controls) ApplyTree(child);
        }

        public static void StyleGrid(DataGridView grid)
        {
            if (!ColorScheme.IsFade) return;
            grid.BorderStyle = BorderStyle.None; grid.BackgroundColor = ColorScheme.FormBackColor;
            grid.GridColor = ColorScheme.BorderSubtle;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.DefaultCellStyle.BackColor = ColorScheme.FormBackColor; grid.DefaultCellStyle.ForeColor = ColorScheme.FormForeColor;
            grid.DefaultCellStyle.SelectionBackColor = ColorScheme.SelectionBackground;
            grid.DefaultCellStyle.SelectionForeColor = ColorScheme.FormForeColor;
            grid.ColumnHeadersDefaultCellStyle.BackColor = ColorScheme.SurfaceSubtle;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ColorScheme.TextSecondary;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = ColorScheme.SurfaceSubtle;
            grid.RowHeadersDefaultCellStyle.BackColor = ColorScheme.FormBackColor;
            grid.RowHeadersDefaultCellStyle.SelectionBackColor = ColorScheme.SelectionBackground;
            grid.RowTemplate.Height = 30;
        }

        private static void StyleButton(Button button)
        {
            if (!ColorScheme.IsFade) return;
            State state = States.GetOrCreateValue(button);
            button.FlatStyle = FlatStyle.Flat; button.UseVisualStyleBackColor = false;
            button.BackColor = state.Role == FadeButtonRole.Primary ? ColorScheme.BrandAmber : ColorScheme.FormBackColor;
            button.ForeColor = state.Role == FadeButtonRole.Primary ? ColorScheme.BrandGraphite :
                state.Role == FadeButtonRole.Danger ? ColorScheme.PreviewCommandKO : ColorScheme.FormForeColor;
            if (!button.Enabled) { button.BackColor = ColorScheme.ControlsBackDisabled; button.ForeColor = ColorScheme.DisabledButtons; }
            button.FlatAppearance.BorderColor = button.Focused ? ColorScheme.FocusRing : ColorScheme.ControlsBorder;
            button.FlatAppearance.BorderSize = button.Focused ? 2 : 1;
            button.FlatAppearance.MouseOverBackColor = state.Role == FadeButtonRole.Primary ? ColorScheme.PrimaryHover : ColorScheme.SelectionBackground;
            button.FlatAppearance.MouseDownBackColor = state.Role == FadeButtonRole.Primary ? ColorScheme.PrimaryPressed : ColorScheme.SurfaceSubtle;
            button.AccessibleName = button.Text.Replace("&", "");
            if (!buttonStateObserved.GetOrCreateValue(button).Observed)
            {
                buttonStateObserved.GetOrCreateValue(button).Observed = true;
                button.GotFocus += (s, e) => StyleButton(button); button.LostFocus += (s, e) => StyleButton(button);
                button.EnabledChanged += (s, e) => StyleButton(button);
                button.Paint += DrawDisabledCaption;
            }
        }
        private static readonly ConditionalWeakTable<Button, State> buttonStateObserved = new ConditionalWeakTable<Button, State>();

        private static void DrawDisabledCaption(object sender, PaintEventArgs e)
        {
            var button = (Button)sender;
            if (!ColorScheme.IsFade || button.Enabled || button.Image != null) return;
            Rectangle bounds = Rectangle.Inflate(button.ClientRectangle, -2, -2);
            using (var background = new SolidBrush(ColorScheme.ControlsBackDisabled)) e.Graphics.FillRectangle(background, bounds);
            TextRenderer.DrawText(e.Graphics, button.Text, button.Font, bounds, ColorScheme.DisabledButtons,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.HidePrefix);
        }

        private static void StyleItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.BackColor = ColorScheme.FormBackColor; item.ForeColor = ColorScheme.FormForeColor;
                if (item is ToolStripButton) item.Padding = new Padding(4, 5, 4, 5);
                ToolStripDropDownItem drop = item as ToolStripDropDownItem;
                if (drop != null) { drop.DropDown.Renderer = Renderer; StyleItems(drop.DropDownItems); }
                ToolStripStatusLabel status = item as ToolStripStatusLabel;
                if (status != null) status.BorderSides = ToolStripStatusLabelBorderSides.None;
            }
        }

        private sealed class FadeRenderer : ToolStripProfessionalRenderer
        {
            public FadeRenderer() : base(new FadeColorTable()) { RoundedEdges = false; }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            { e.TextColor = e.Item.Enabled ? ColorScheme.FormForeColor : ColorScheme.DisabledButtons; base.OnRenderItemText(e); }
        }
        private sealed class FadeColorTable : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin => ColorScheme.FormBackColor;
            public override Color ToolStripGradientMiddle => ColorScheme.FormBackColor;
            public override Color ToolStripGradientEnd => ColorScheme.FormBackColor;
            public override Color MenuStripGradientBegin => ColorScheme.FormBackColor;
            public override Color MenuStripGradientEnd => ColorScheme.FormBackColor;
            public override Color StatusStripGradientBegin => ColorScheme.FormBackColor;
            public override Color StatusStripGradientEnd => ColorScheme.FormBackColor;
            public override Color ToolStripDropDownBackground => ColorScheme.FormBackColor;
            public override Color ImageMarginGradientBegin => ColorScheme.FormBackColor;
            public override Color ImageMarginGradientMiddle => ColorScheme.FormBackColor;
            public override Color ImageMarginGradientEnd => ColorScheme.FormBackColor;
            public override Color ButtonSelectedHighlight => ColorScheme.SelectionBackground;
            public override Color ButtonSelectedGradientBegin => ColorScheme.SelectionBackground;
            public override Color ButtonSelectedGradientMiddle => ColorScheme.SelectionBackground;
            public override Color ButtonSelectedGradientEnd => ColorScheme.SelectionBackground;
            public override Color ButtonCheckedGradientBegin => ColorScheme.SelectionBackground;
            public override Color ButtonCheckedGradientMiddle => ColorScheme.SelectionBackground;
            public override Color ButtonCheckedGradientEnd => ColorScheme.SelectionBackground;
            public override Color ButtonPressedGradientBegin => ColorScheme.SurfaceSubtle;
            public override Color ButtonPressedGradientMiddle => ColorScheme.SurfaceSubtle;
            public override Color ButtonPressedGradientEnd => ColorScheme.SurfaceSubtle;
            public override Color ButtonSelectedBorder => ColorScheme.FocusRing;
            public override Color ButtonCheckedHighlightBorder => ColorScheme.FocusRing;
            public override Color MenuItemSelected => ColorScheme.SelectionBackground;
            public override Color MenuItemBorder => ColorScheme.FocusRing;
            public override Color MenuItemSelectedGradientBegin => ColorScheme.SelectionBackground;
            public override Color MenuItemSelectedGradientEnd => ColorScheme.SelectionBackground;
            public override Color MenuItemPressedGradientBegin => ColorScheme.SurfaceSubtle;
            public override Color MenuItemPressedGradientMiddle => ColorScheme.SurfaceSubtle;
            public override Color MenuItemPressedGradientEnd => ColorScheme.SurfaceSubtle;
            public override Color MenuBorder => ColorScheme.BorderSubtle;
            public override Color ToolStripBorder => ColorScheme.BorderSubtle;
            public override Color SeparatorDark => ColorScheme.BorderSubtle;
            public override Color SeparatorLight => ColorScheme.FormBackColor;
            public override Color CheckBackground => ColorScheme.SelectionBackground;
            public override Color CheckSelectedBackground => ColorScheme.SelectionBackground;
            public override Color CheckPressedBackground => ColorScheme.SelectionBackground;
        }
    }
}
