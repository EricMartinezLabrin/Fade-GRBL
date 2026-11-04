using System;
using System.Drawing;
using System.Windows.Forms;
using LaserGRBL.Layering;

namespace LaserGRBL
{
    public partial class ConnectLogForm
    {
        private sealed class BufferedJobPanel : Panel { public BufferedJobPanel() { DoubleBuffered = true; ResizeRedraw = true; } }
        private sealed class BufferedJobTable : TableLayoutPanel { public BufferedJobTable() { DoubleBuffered = true; ResizeRedraw = true; } }
        private Label jobSummary, jobRemaining;
        private Button jobPause, jobResume, jobParameters;

        private void BuildJobControls()
        {
            tableLayoutPanel1.RowStyles[1].SizeType = SizeType.AutoSize;
            GBFile.AutoSize = true; GBFile.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tableLayoutPanel5.AutoSize = true; tableLayoutPanel5.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tableLayoutPanel5.RowCount = 5;
            while (tableLayoutPanel5.RowStyles.Count < 5) tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            jobSummary = new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 2) };
            jobRemaining = new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 2, 0, 4) };
            FlowLayoutPanel actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
            jobPause = JobButton("JobPause", () => Core.FeedHold(false));
            jobResume = JobButton("JobResume", () => Core.ShowJobResumeDialog(ParentForm));
            jobParameters = JobButton("JobParameters", () => Core.ShowJobParametersDialog(ParentForm));
            actions.Controls.AddRange(new Control[] { jobPause, jobResume, jobParameters });
            tableLayoutPanel5.Controls.Add(jobSummary, 0, 2); tableLayoutPanel5.SetColumnSpan(jobSummary, 5);
            tableLayoutPanel5.Controls.Add(jobRemaining, 0, 3); tableLayoutPanel5.SetColumnSpan(jobRemaining, 5);
            tableLayoutPanel5.Controls.Add(actions, 0, 4); tableLayoutPanel5.SetColumnSpan(actions, 5);
            tableLayoutPanel5.SizeChanged += (s, e) =>
            {
                Size maximum = new Size(Math.Max(100, tableLayoutPanel5.ClientSize.Width - 8), 0);
                if (jobSummary.MaximumSize != maximum) jobSummary.MaximumSize = jobRemaining.MaximumSize = maximum;
            };
            TT.SetToolTip(jobRemaining, LayerText.Get("JobEstimateExplanation"));
            TT.SetToolTip(jobParameters, LayerText.Get("JobAdvancedUnavailable"));
        }

        private Button JobButton(string key, Action action)
        {
            Button button = new Button { AutoSize = true, Text = LayerText.Get(key), UseVisualStyleBackColor = true };
            button.Click += (s, e) => action(); return button;
        }

        private bool UpdateJobControls()
        {
            JobProgressSnapshot progress = Core.GetJobProgress();
            bool active = progress != null && Core.JobEditingLocked;
            jobPause.Enabled = active && Core.CanFeedHold && progress.State == JobExecutionState.Running;
            jobResume.Enabled = active && progress.State == JobExecutionState.Paused && Core.CanResumeHold;
            jobParameters.Enabled = Core.CanAdjustJob;
            UDLoopCounter.Enabled = !active;
            if (progress == null) { SetJobText(jobSummary, LayerText.Get("JobReady")); SetJobText(jobRemaining, ""); return false; }
            string state = LayerText.Get("JobState" + progress.State);
            string summary = string.Format(LayerText.Get("JobProgress"), progress.Percent) + " \u00b7 " + state;
            if (progress.LayerName != null)
                summary += Environment.NewLine + (progress.ObjectIndex >= 0 ?
                    string.Format(LayerText.Get("JobObjectProgress"), progress.LayerName, progress.ObjectIndex + 1, progress.Pass, progress.Passes, progress.CompletedPasses) :
                    string.Format(LayerText.Get("JobLayerProgress"), progress.LayerName, progress.Pass, progress.Passes, progress.CompletedPasses));
            if (progress.Cycles > 1) summary += Environment.NewLine + string.Format(LayerText.Get("JobCycles"), progress.Cycle, progress.Cycles);
            string remaining = progress.State == JobExecutionState.Interrupted ? LayerText.Get("JobStateInterrupted") :
                string.Format(LayerText.Get("JobRemaining"), progress.Remaining.HasValue ? FormatJobTime(progress.Remaining.Value) : LayerText.Get("JobCalculating"));
            if (Core.HasStagedJobParameters) remaining += Environment.NewLine + LayerText.Get("JobStaged");
            string error = Core.JobControlError;
            if (!string.IsNullOrEmpty(error)) remaining += Environment.NewLine + error;
            tableLayoutPanel5.SuspendLayout();
            try { SetJobText(jobSummary, summary); SetJobText(jobRemaining, remaining); }
            finally { tableLayoutPanel5.ResumeLayout(true); }
            UpdateProgressBar(100, progress.SentPercent, progress.Percent, string.Format("{0:0} %", progress.Percent));
            return true;
        }

        private static void SetJobText(Label label, string text) { if (label.Text != text) label.Text = text; }
        private void UpdateProgressBar(double maximum, double sent, double executed, string text)
        {
            bool changed = PB.Maximum != maximum || PB.Bars[0].Value != sent || PB.Bars[1].Value != executed || PB.PercString != text;
            if (!changed) return;
            PB.Maximum = maximum;
            PB.Bars[0].Value = sent;
            PB.Bars[1].Value = executed;
            PB.PercString = text;
            PB.Invalidate();
        }

        private static string FormatJobTime(TimeSpan time)
        { return string.Format("{0:00}:{1:00}:{2:00}", (long)time.TotalHours, time.Minutes, time.Seconds); }
    }
}
