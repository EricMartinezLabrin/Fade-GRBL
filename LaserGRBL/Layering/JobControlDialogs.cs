using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace LaserGRBL.Layering
{
    internal sealed class JobPassOrderDialog : Form
    {
        private readonly RadioButton objects;
        public JobPassOrder PassOrder { get { return objects.Checked ? JobPassOrder.ByObject : JobPassOrder.ByLayer; } }

        public JobPassOrderDialog(JobPassOrder initial)
        {
            Text = LayerText.Get("PassOrderTitle"); StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = MaximizeBox = false;
            ClientSize = new Size(560, 300); AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
            for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(530, 0), Text = LayerText.Get("PassOrderQuestion"), Margin = new Padding(3, 3, 3, 12) });
            objects = new RadioButton { AutoSize = true, Text = LayerText.Get("PassOrderObjects"), Checked = initial == JobPassOrder.ByObject };
            layout.Controls.Add(objects);
            layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(510, 0), Margin = new Padding(22, 0, 3, 12), Text = LayerText.Get("PassOrderObjectsExplanation") });
            layout.Controls.Add(new RadioButton { AutoSize = true, Text = LayerText.Get("PassOrderLayers"), Checked = initial != JobPassOrder.ByObject });
            layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(510, 0), Margin = new Padding(22, 0, 3, 12), Text = LayerText.Get("PassOrderLayersExplanation") });
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var start = new Button { Text = LayerText.Get("PassOrderStart"), AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = LayerText.Get("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(start); buttons.Controls.Add(cancel); layout.Controls.Add(buttons);
            Controls.Add(layout); AcceptButton = start; CancelButton = cancel;
        }
    }

    internal sealed class JobResumeDialog : Form
    {
        private readonly RadioButton rewind = new RadioButton();
        private readonly NumericUpDown count = new NumericUpDown();
        public int Movements { get { return rewind.Checked ? (int)count.Value : 0; } }

        public JobResumeDialog(JobExecutionSession session, bool supported, bool staged)
        {
            Text = LayerText.Get("JobResume"); StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = MaximizeBox = false;
            ClientSize = new Size(530, 255); AutoScaleMode = AutoScaleMode.Font;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            RadioButton current = new RadioButton { AutoSize = true, Checked = true, Text = LayerText.Get("JobContinueHere"), Padding = new Padding(0, 6, 0, 6) };
            rewind.Text = LayerText.Get("JobRewind"); rewind.AutoSize = true;
            int end = session.HasReportedLine ? session.CurrentIndex : session.SentIndex;
            int limit = session.Commands.Skip(session.PassStart).Take(Math.Max(0, end - session.PassStart)).Count(c => c.IsCuttingMovement);
            rewind.Enabled = supported && limit > 0;
            count.Minimum = 1; count.Maximum = Math.Max(1, limit); count.Value = Math.Min(5, count.Maximum); count.Enabled = false;
            count.Width = 90;
            rewind.CheckedChanged += (s, e) => count.Enabled = rewind.Checked;
            FlowLayoutPanel amount = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            amount.Controls.Add(count); amount.Controls.Add(new Label { AutoSize = true, Text = string.Format(LayerText.Get("JobRewindLimit"), limit) });
            string explanation = !supported ? LayerText.Get("JobAdvancedUnavailable") : !session.HasReportedLine ?
                string.Format(LayerText.Get("JobSafeFallback"), limit) : LayerText.Get("JobRewindExplanation");
            if (staged) explanation += Environment.NewLine + LayerText.Get("JobStaged");
            Label note = new Label { AutoSize = false, Dock = DockStyle.Fill, Text = explanation, Padding = new Padding(0, 8, 0, 0) };
            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            Button cancel = new Button { AutoSize = true, Text = LayerText.Get("Cancel"), DialogResult = DialogResult.Cancel };
            Button resume = new Button { AutoSize = true, Text = LayerText.Get("JobResume"), DialogResult = DialogResult.OK };
            buttons.Controls.Add(cancel); buttons.Controls.Add(resume);
            layout.Controls.Add(current); layout.Controls.Add(rewind); layout.Controls.Add(amount); layout.Controls.Add(note); layout.Controls.Add(buttons);
            Controls.Add(layout); AcceptButton = resume; CancelButton = cancel;
        }
    }

    internal sealed class JobParametersDialog : Form
    {
        private readonly DataGridView grid = new DataGridView();
        private readonly JobExecutionSession session;
        public LaserDocument Document { get; private set; }

        public JobParametersDialog(LaserDocument document, JobExecutionSession job)
        {
            Document = document.Clone(); session = job;
            Text = LayerText.Get("JobParameters"); StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(540, 300); Size = new Size(680, 420); AutoScaleMode = AutoScaleMode.Font;
            grid.Dock = DockStyle.Fill; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = false; grid.RowHeadersVisible = false;
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Layer", HeaderText = LayerText.Get("JobLayer"), ReadOnly = true });
            grid.Columns.Add("Power", LayerText.Get("JobPower")); grid.Columns.Add("Speed", LayerText.Get("JobSpeed")); grid.Columns.Add("Passes", LayerText.Get("JobPasses"));
            foreach (LaserLayer layer in Document.Layers.Where(l => l.OutputEnabled).OrderBy(l => l.Order))
            {
                int index = grid.Rows.Add(layer.Name, layer.PowerPercent, layer.SpeedMmPerMinute, layer.Passes);
                DataGridViewRow row = grid.Rows[index]; row.Tag = layer;
                row.ReadOnly = session.IsLayerCompleted(layer.Id);
                if (row.ReadOnly) { row.DefaultCellStyle.ForeColor = SystemColors.GrayText; row.Cells[0].ToolTipText = LayerText.Get("JobLayerCompleted"); }
            }
            grid.DataError += (s, e) => { e.ThrowException = false; };
            Label note = new Label { Dock = DockStyle.Top, Height = 58, Padding = new Padding(8), Text = LayerText.Get("JobParameterExplanation") };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
            Button cancel = new Button { AutoSize = true, Text = LayerText.Get("Cancel"), DialogResult = DialogResult.Cancel };
            Button apply = new Button { AutoSize = true, Text = LayerText.Get("JobApply") };
            apply.Click += Apply;
            buttons.Controls.Add(cancel); buttons.Controls.Add(apply);
            Controls.Add(grid); Controls.Add(note); Controls.Add(buttons); CancelButton = cancel; AcceptButton = apply;
        }

        private void Apply(object sender, EventArgs args)
        {
            if (!grid.EndEdit()) return;
            foreach (DataGridViewRow row in grid.Rows)
            {
                LaserLayer layer = (LaserLayer)row.Tag;
                if (row.ReadOnly) continue;
                double power; int speed, passes;
                int minimum = session.CurrentCommand != null && session.CurrentCommand.SourceLayerId == layer.Id ?
                    Math.Max(1, (session.CurrentCommand.SourcePass + session.ObjectRepetitions - 1) / session.ObjectRepetitions) : 1;
                bool valid = double.TryParse(Convert.ToString(row.Cells["Power"].Value), NumberStyles.Float, CultureInfo.CurrentCulture, out power) &&
                    !double.IsNaN(power) && !double.IsInfinity(power) && power >= 0 && power <= 100;
                valid &= int.TryParse(Convert.ToString(row.Cells["Speed"].Value), out speed) && speed >= 1 && speed <= 100000;
                valid &= int.TryParse(Convert.ToString(row.Cells["Passes"].Value), out passes) && passes >= minimum && passes <= 99;
                if (!valid)
                {
                    MessageBox.Show(this, string.Format(LayerText.Get("JobInvalidParameters"), layer.Name, minimum), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                layer.PowerPercent = power; layer.SpeedMmPerMinute = speed; layer.Passes = passes;
            }
            Document.IsDirty = true; DialogResult = DialogResult.OK;
        }
    }
}
