using System;
using System.Drawing;
using System.Windows.Forms;
using LaserGRBL.Layering;
using LaserGRBL.UserControls;

namespace LaserGRBL
{
    public partial class ConnectLogForm
    {
        private Button designStart, designStop, designOpen, designSave, consoleToggle;

        internal Control CreateDesignJobPanel()
        {
            Font = FadeTheme.BodyFont;
            GBFile.Parent.Controls.Remove(GBFile);
            tableLayoutPanel1.RowStyles[1] = new RowStyle(SizeType.Absolute, 0);
            GBFile.Padding = new Padding(12, 8, 12, 8); GBFile.Margin = Padding.Empty;
            tableLayoutPanel5.SuspendLayout();
            tableLayoutPanel5.Controls.Remove(BtnRunProgram); tableLayoutPanel5.Controls.Remove(BtnAbortProgram); tableLayoutPanel5.Controls.Remove(BtnOpen);
            tableLayoutPanel5.ColumnStyles.Clear();
            tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
            tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            designOpen = DesignAction("DesignOpen", (s, e) => BtnOpenClick(s, e));
            designSave = DesignAction("DesignSave", (s, e) => Core.SaveProject(ParentForm));
            designStart = DesignAction("DesignStart", (s, e) => { designStart.Enabled = false; BtnRunProgramClick(s, e); });
            designStop = DesignAction("DesignStop", (s, e) => BtnAbortProgram_Click(s, e));
            tableLayoutPanel5.Controls.Add(designOpen, 3, 0); tableLayoutPanel5.Controls.Add(designSave, 4, 0);
            tableLayoutPanel5.Controls.Add(designStart, 3, 1); tableLayoutPanel5.Controls.Add(designStop, 4, 1);
            PB.MinimumSize = new Size(100, 28); PB.Dock = DockStyle.Fill;
            UDLoopCounter.Dock = DockStyle.Fill;
            TT.SetToolTip(UDLoopCounter, LayerText.Get("DesignRepetitions"));
            var actions = (FlowLayoutPanel)tableLayoutPanel5.GetControlFromPosition(0, 4);
            tableLayoutPanel5.Controls.Remove(jobSummary); tableLayoutPanel5.Controls.Remove(jobRemaining); tableLayoutPanel5.Controls.Remove(actions);
            tableLayoutPanel5.RowCount = 2; tableLayoutPanel5.RowStyles.Clear();
            tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            jobSummary.MinimumSize = new Size(0, 32); jobRemaining.MinimumSize = new Size(0, 16);
            jobSummary.Padding = jobRemaining.Padding = Padding.Empty;
            jobSummary.Margin = jobRemaining.Margin = Padding.Empty;
            jobSummary.Font = FadeTheme.CaptionFont; jobRemaining.Font = FadeTheme.CaptionFont;
            tableLayoutPanel5.ResumeLayout(true);
            var text = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            text.Controls.Add(jobSummary); text.Controls.Add(jobRemaining);
            text.SizeChanged += (s, e) =>
            {
                var maximum = new Size(Math.Max(100, text.ClientSize.Width - 8), 0);
                if (jobSummary.MaximumSize != maximum) jobSummary.MaximumSize = jobRemaining.MaximumSize = maximum;
            };
            actions.WrapContents = false; actions.Dock = DockStyle.Fill; actions.FlowDirection = FlowDirection.RightToLeft; actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var details = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            details.SetColumnSpan(actions, 1); details.SetRowSpan(actions, 1);
            details.Controls.Add(text, 0, 0); details.Controls.Add(actions, 1, 0);
            var jobLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            jobLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            jobLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); jobLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            GBFile.Controls.Remove(tableLayoutPanel5); tableLayoutPanel5.Dock = DockStyle.Fill;
            jobLayout.Controls.Add(tableLayoutPanel5, 0, 0); jobLayout.Controls.Add(details, 0, 1); GBFile.Controls.Add(jobLayout);

            consoleToggle = DesignAction("DesignConsoleHide", (s, e) =>
            {
                GBCommands.Visible = !GBCommands.Visible;
                consoleToggle.Text = LayerText.Get(GBCommands.Visible ? "DesignConsoleHide" : "DesignConsoleShow");
            });
            var consoleLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            consoleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            consoleLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); consoleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Remove(tableLayoutPanel1); consoleLayout.Controls.Add(tableLayoutPanel1, 0, 0);
            consoleLayout.Controls.Add(consoleToggle, 0, 1); Controls.Add(consoleLayout);
            TxtManualCommand.Font = FadeTheme.CodeFont;
            RefreshDesignTheme();
            if (Core != null) { UpdateDesignActions(); UpdateJobControls(); }
            return GBFile;
        }
        private static Button DesignAction(string key, EventHandler click)
        {
            var button = new Button { Text = LayerText.Get(key), AutoSize = true, Dock = DockStyle.Fill, MinimumSize = new Size(80, 32), Margin = new Padding(4), Font = FadeTheme.BodyFont };
            button.Click += click; return button;
        }
        internal void RefreshDesignTheme()
        {
            if (designStart == null) return;
            FadeTheme.Apply(GBFile); FadeTheme.Apply(this);
            FadeTheme.SetButtonRole(designStart, FadeButtonRole.Primary);
            FadeTheme.SetButtonRole(designStop, FadeButtonRole.Danger);
            FadeTheme.SetButtonRole(jobResume, FadeButtonRole.Primary);
            jobRemaining.ForeColor = ColorScheme.TextSecondary;
            PB.BorderColor = ColorScheme.BorderSubtle;
        }
        private void UpdateDesignActions()
        {
            if (designStart == null) return;
            designStart.Enabled = Core.CanSendFile; designStop.Enabled = Core.CanAbortProgram;
            designOpen.Enabled = Core.CanLoadNewFile;
            designSave.Enabled = (Core.HasProgram || Core.LayerController.IsLayered) &&
                (!Core.LayerController.IsLayered || (Core.LayerController.IsCompilationCurrent && !Core.LayerController.HasCompilationErrors));
        }
    }
}
