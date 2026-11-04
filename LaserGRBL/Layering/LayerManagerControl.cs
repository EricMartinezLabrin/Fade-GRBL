using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LaserGRBL.Layering
{
    public class LayerManagerControl : UserControl
    {
        private readonly ToolStrip tools = new ToolStrip();
        private sealed class LayerGrid : DataGridView
        {
            public Action<Keys> ClipboardCommand;
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (!IsCurrentCellInEditMode && ClipboardCommand != null &&
                    (keyData == (Keys.Control | Keys.C) || keyData == (Keys.Control | Keys.V) || keyData == (Keys.Control | Keys.D)))
                {
                    ClipboardCommand(keyData); return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
        }
        private readonly LayerGrid grid = new LayerGrid();
        private readonly Label status = new Label();
        private GrblCore core;
        private bool refreshing;
        private bool refreshPending;
        private Point dragStart;
        public event Action<bool> CollapseChanged;
        public event Action<bool, IList<LaserLayer>> CopiesInserted;
        public event Action<IList<LaserLayer>> SelectedLayersChanged;
        public bool IsCollapsed { get; private set; }

        public LayerManagerControl()
        {
            Dock = DockStyle.Fill;
            MinimumSize = new Size(34, 0);
            BuildToolbar();
            BuildGrid();
            status.Dock = DockStyle.Bottom;
            status.Height = 22;
            status.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(grid); Controls.Add(status); Controls.Add(tools);
            ApplyTheme();
        }

        public void SetCore(GrblCore value)
        {
            if (core != null) { core.LayerController.DocumentChanged -= RefreshRows; core.LayerController.CompilationStateChanged -= RefreshState; }
            core = value;
            if (core != null) { core.LayerController.DocumentChanged += RefreshRows; core.LayerController.CompilationStateChanged += RefreshState; }
            RefreshRows();
        }

        public void ApplyTheme()
        {
            BackColor = grid.BackgroundColor = ColorScheme.FormBackColor;
            ForeColor = grid.ForeColor = status.ForeColor = ColorScheme.FormForeColor;
            tools.BackColor = ColorScheme.FormBackColor; tools.ForeColor = ColorScheme.FormForeColor;
            grid.DefaultCellStyle.BackColor = ColorScheme.FormBackColor; grid.DefaultCellStyle.ForeColor = ColorScheme.FormForeColor;
            grid.DefaultCellStyle.SelectionBackColor = ColorScheme.MenuHighlightColor; grid.DefaultCellStyle.SelectionForeColor = ColorScheme.FormForeColor;
            grid.ColumnHeadersDefaultCellStyle.BackColor = ColorScheme.FormBackColor; grid.ColumnHeadersDefaultCellStyle.ForeColor = ColorScheme.FormForeColor;
            grid.EnableHeadersVisualStyles = false;
        }

        private void BuildToolbar()
        {
            tools.Dock = DockStyle.Top; tools.GripStyle = ToolStripGripStyle.Hidden;
            AddButton("+", LayerText.Get("NewLayer"), (s, e) => { if (CanEdit()) SelectLayer(core.LayerController.AddLayer()); });
            AddButton(LayerText.Get("Copy"), "Ctrl+C", (s, e) => CopySelection());
            AddButton(LayerText.Get("Paste"), "Ctrl+V", (s, e) => PasteSelection(false));
            AddButton(LayerText.Get("Duplicate"), "Ctrl+D", (s, e) => PasteSelection(true));
            AddButton("-", LayerText.Get("DeleteLayer"), DeleteSelected);
            tools.Items.Add(new ToolStripSeparator());
            AddButton("Up", LayerText.Get("MoveUp"), (s, e) => MoveSelected(-1)); AddButton("Down", LayerText.Get("MoveDown"), (s, e) => MoveSelected(1));
            AddButton("Edit", LayerText.Get("Advanced"), EditAdvanced); tools.Items.Add(new ToolStripSeparator()); AddButton("<", LayerText.Get("Collapse"), ToggleCollapsed);
        }

        private void AddButton(string text, string tooltip, EventHandler handler)
        {
            ToolStripButton button = new ToolStripButton(text) { ToolTipText = tooltip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += handler; tools.Items.Add(button);
        }

        private void BuildGrid()
        {
            grid.ClipboardCommand = keys =>
            {
                if (keys == (Keys.Control | Keys.C)) CopySelection();
                else PasteSelection(keys == (Keys.Control | Keys.D));
                UpdateJobLock();
            };
            grid.Dock = DockStyle.Fill; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
            grid.AllowDrop = true;
            grid.RowHeadersWidth = 24; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = true; grid.AutoGenerateColumns = false;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Output", HeaderText = "Out", Width = 34 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Visible", HeaderText = "Eye", Width = 34 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LayerName", HeaderText = "Layer", Width = 90 });
            grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Mode", HeaderText = "Mode", Width = 78, FlatStyle = FlatStyle.Flat });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Speed", HeaderText = "mm/min", Width = 58 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Power", HeaderText = "%", Width = 38 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Passes", HeaderText = "Pass", Width = 38 });
            grid.CellValueChanged += GridCellValueChanged;
            grid.SelectionChanged += (s, e) => RaiseSelectionChanged();
            grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty && (grid.CurrentCell is DataGridViewCheckBoxCell || grid.CurrentCell is DataGridViewComboBoxCell)) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValidating += ValidateCell;
            grid.CellEndEdit += (s, e) => { if (refreshPending) BeginInvoke((MethodInvoker)RefreshRows); };
            grid.RowHeaderMouseDoubleClick += ChangeColor; grid.DataError += (s, e) => { e.ThrowException = false; };
            grid.MouseDown += (s, e) => dragStart = e.Location;
            grid.MouseMove += GridMouseMove;
            grid.DragOver += (s, e) => e.Effect = e.Data.GetDataPresent(typeof(LaserLayer)) ? DragDropEffects.Move : DragDropEffects.None;
            grid.DragDrop += GridDragDrop;
        }

        private void RefreshRows()
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)RefreshRows); return; }
            if (grid.IsCurrentCellInEditMode) { refreshPending = true; RefreshState(); return; }
            refreshPending = false;
            refreshing = true;
            HashSet<Guid> selected = new HashSet<Guid>(SelectedLayers().Select(layer => layer.Id));
            LaserLayer currentLayer = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as LaserLayer;
            string currentColumn = grid.CurrentCell == null ? null : grid.CurrentCell.OwningColumn.Name;
            int firstRow = grid.FirstDisplayedScrollingRowIndex;
            grid.Rows.Clear(); bool layered = core != null && core.LayerController.IsLayered; grid.Enabled = layered;
            if (!layered) { status.Text = core == null ? "" : LayerText.Get("DirectGcode"); refreshing = false; return; }
            foreach (LaserLayer layer in core.LayerController.Document.Layers.OrderBy(l => l.Order))
            {
                DataGridViewRow row = grid.Rows[grid.Rows.Add()]; row.Tag = layer;
                row.Cells["Output"].Value = layer.OutputEnabled; row.Cells["Visible"].Value = layer.Visible; row.Cells["LayerName"].Value = layer.Name;
                DataGridViewComboBoxCell modeCell = (DataGridViewComboBoxCell)row.Cells["Mode"];
                if (layer.ContentKind == LayerContentKind.Raster) modeCell.Items.Add(LayerMode.Image); else { modeCell.Items.Add(LayerMode.Line); modeCell.Items.Add(LayerMode.Fill); modeCell.Items.Add(LayerMode.OffsetFill); }
                modeCell.Value = layer.Mode; row.Cells["Speed"].Value = layer.SpeedMmPerMinute; row.Cells["Power"].Value = layer.PowerPercent.ToString("0.#"); row.Cells["Passes"].Value = layer.Passes;
                row.HeaderCell.Style.BackColor = layer.Color; row.HeaderCell.ToolTipText = LayerText.Get("ChangeColor");
                if (layer.HasError) row.ErrorText = layer.StatusMessage; else if (!string.IsNullOrEmpty(layer.StatusMessage)) row.Cells["LayerName"].ToolTipText = layer.StatusMessage;
                if (selected.Contains(layer.Id)) row.Selected = true;
            }
            if (currentLayer != null && currentColumn != null)
            {
                DataGridViewRow currentRow = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => ((LaserLayer)row.Tag).Id == currentLayer.Id);
                if (currentRow != null) grid.CurrentCell = currentRow.Cells[currentColumn];
            }
            foreach (DataGridViewRow row in grid.Rows) row.Selected = selected.Contains(((LaserLayer)row.Tag).Id);
            if (firstRow >= 0 && firstRow < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = firstRow;
            RefreshState(); refreshing = false;
            RaiseSelectionChanged();
        }

        private void RefreshState()
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)RefreshState); return; }
            if (core == null || !core.LayerController.IsLayered) return;
            status.Text = !core.LayerController.IsCompilationCurrent ? LayerText.Get("Updating") : core.LayerController.HasCompilationErrors ? LayerText.Get("FixErrors") : string.Format(LayerText.Get("LayerCount"), core.LayerController.Document.Layers.Count);
        }

        private void GridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (refreshing || e.RowIndex < 0 || !CanEdit()) return;
            DataGridViewRow row = grid.Rows[e.RowIndex]; LaserLayer layer = row.Tag as LaserLayer; if (layer == null) return;
            try
            {
                layer.OutputEnabled = Convert.ToBoolean(row.Cells["Output"].Value); layer.Visible = Convert.ToBoolean(row.Cells["Visible"].Value);
                layer.Name = Convert.ToString(row.Cells["LayerName"].Value); layer.Mode = (LayerMode)row.Cells["Mode"].Value;
                layer.SpeedMmPerMinute = Convert.ToInt32(row.Cells["Speed"].Value); layer.PowerPercent = Convert.ToDouble(row.Cells["Power"].Value); layer.Passes = Convert.ToInt32(row.Cells["Passes"].Value);
                // Never rebuild rows inside DataGridView's commit/end-edit event stack.
                BeginInvoke((MethodInvoker)(() => core.LayerController.Changed()));
            }
            catch { RefreshRows(); }
        }

        private void ValidateCell(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (refreshing || e.RowIndex < 0) return;
            string column = grid.Columns[e.ColumnIndex].Name;
            string value = Convert.ToString(e.FormattedValue);
            int integer; double power;
            bool valid = true;
            if (column == "Speed" || column == "Passes") valid = int.TryParse(value, out integer) && integer > 0 && integer <= (column == "Speed" ? 100000 : 99);
            if (column == "Power") valid = double.TryParse(value, out power) && !double.IsNaN(power) && power >= 0 && power <= 100;
            e.Cancel = !valid;
            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = valid ? "" : LayerText.Get("InvalidValue");
        }

        public bool IsEditingText { get { return grid.IsCurrentCellInEditMode; } }

        private void CopySelection() { if (core != null && core.LayerController.IsLayered) core.LayerController.CopyLayers(SelectedLayers()); }
        private void PasteSelection(bool duplicate)
        {
            if (!CanEdit()) return;
            bool objects = !duplicate && core.LayerController.Clipboard.ContainsObjects;
            IList<LaserLayer> copies = duplicate ? core.LayerController.DuplicateLayers(SelectedLayers()) : core.LayerController.Paste();
            if (copies.Count == 0) return;
            SelectLayers(copies);
            CopiesInserted?.Invoke(objects, copies);
        }
        private bool CanEdit() { return core != null && !core.JobEditingLocked && core.LayerController.IsLayered; }
        public void UpdateJobLock()
        {
            bool locked = core != null && core.JobEditingLocked;
            grid.ReadOnly = locked;
            grid.AllowDrop = !locked;
            foreach (ToolStripItem item in tools.Items)
            {
                if (!(item is ToolStripButton) || item.ToolTipText == LayerText.Get("Collapse")) continue;
                bool layered = core != null && core.LayerController.IsLayered;
                if (item.ToolTipText == "Ctrl+C") item.Enabled = layered && SelectedLayers().Count > 0;
                else if (item.ToolTipText == "Ctrl+V") item.Enabled = layered && !locked && core.LayerController.Clipboard.HasContent;
                else if (item.ToolTipText == "Ctrl+D") item.Enabled = layered && !locked && SelectedLayers().Count > 0;
                else item.Enabled = layered && !locked;
            }
        }
        public IList<LaserLayer> SelectedLayers() { return grid.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Tag as LaserLayer).Where(layer => layer != null).OrderBy(layer => layer.Order).ToList(); }
        public LaserLayer SelectedLayer() { return SelectedLayers().FirstOrDefault(); }
        public void SelectAllLayers()
        {
            if (core == null || core.LayerController.Document == null)
                SelectLayers(new LaserLayer[0]);
            else
                SelectLayers(core.LayerController.Document.Layers);
        }
        public void SelectLayers(IEnumerable<LaserLayer> layers)
        {
            HashSet<Guid> ids = new HashSet<Guid>((layers ?? Enumerable.Empty<LaserLayer>()).Where(layer => layer != null).Select(layer => layer.Id));
            refreshing = true;
            grid.ClearSelection();
            foreach (DataGridViewRow row in grid.Rows)
            {
                LaserLayer layer = row.Tag as LaserLayer;
                if (layer != null && ids.Contains(layer.Id)) row.Selected = true;
            }
            if (grid.SelectedRows.Count > 0) grid.CurrentCell = grid.SelectedRows[0].Cells["LayerName"];
            refreshing = false;
            RaiseSelectionChanged();
        }
        private void SelectLayer(LaserLayer layer) { SelectLayers(new[] { layer }); }
        private void RaiseSelectionChanged() { if (!refreshing && SelectedLayersChanged != null) SelectedLayersChanged(SelectedLayers()); }
        private void DeleteSelected(object sender, EventArgs e) { if (!CanEdit()) return; LaserLayer layer = SelectedLayer(); if (layer == null) return; if (layer.HasContent && MessageBox.Show(LayerText.Get("DeleteConfirm"), LayerText.Get("Layers"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return; core.LayerController.Remove(layer); }
        private void MoveSelected(int delta) { if (!CanEdit()) return; LaserLayer layer = SelectedLayer(); if (layer != null) { core.LayerController.Move(layer, delta); SelectLayer(layer); } }

        private void GridMouseMove(object sender, MouseEventArgs e)
        {
            if (!CanEdit()) return;
            if (e.Button != MouseButtons.Left || Math.Abs(e.X - dragStart.X) < SystemInformation.DragSize.Width / 2 || Math.Abs(e.Y - dragStart.Y) < SystemInformation.DragSize.Height / 2) return;
            DataGridView.HitTestInfo hit = grid.HitTest(dragStart.X, dragStart.Y);
            if (hit.RowIndex >= 0) grid.DoDragDrop(grid.Rows[hit.RowIndex].Tag, DragDropEffects.Move);
        }

        private void GridDragDrop(object sender, DragEventArgs e)
        {
            if (!CanEdit()) return;
            LaserLayer layer = e.Data.GetData(typeof(LaserLayer)) as LaserLayer;
            Point client = grid.PointToClient(new Point(e.X, e.Y));
            DataGridView.HitTestInfo hit = grid.HitTest(client.X, client.Y);
            if (layer == null || hit.RowIndex < 0) return;
            core.LayerController.MoveTo(layer, hit.RowIndex);
            SelectLayer(layer);
        }

        private void ChangeColor(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (!CanEdit() || e.RowIndex < 0) return;
            LaserLayer layer = grid.Rows[e.RowIndex].Tag as LaserLayer; if (layer == null) return;
            Color selectedColor;
            if (LayerColorPickerForm.TryChoose(this, layer.Color, out selectedColor))
            {
                if (selectedColor.ToArgb() == layer.ColorArgb) return;
                LaserLayer conflict = core.LayerController.Document.Layers.FirstOrDefault(l => l != layer && l.Color.ToArgb() == selectedColor.ToArgb());
                if (conflict != null)
                {
                    if (conflict.ContentKind != layer.ContentKind || MessageBox.Show(LayerText.Get("MergeConfirm"), LayerText.Get("Layers"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                    if (layer.ContentKind != LayerContentKind.Vector) return; conflict.VectorItems.AddRange(layer.VectorItems); core.LayerController.Remove(layer);
                }
                else { layer.Color = selectedColor; core.LayerController.Changed(); }
            }
        }

        private void EditAdvanced(object sender, EventArgs e)
        {
            if (!CanEdit()) return;
            LaserLayer layer = SelectedLayer(); if (layer == null) return;
            if (layer.ContentKind == LayerContentKind.Raster)
            {
                if (MessageBox.Show(LayerText.Get("ImageEditConfirm"), LayerText.Get("ImageLayer"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                    core.LayerController.ReprocessRaster(layer);
                return;
            }
            using (Form form = new Form { Text = LayerText.Get("AdvancedTitle"), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(330, 190), BackColor = ColorScheme.FormBackColor, ForeColor = ColorScheme.FormForeColor })
            {
                NumericUpDown interval = AddNumeric(form, LayerText.Get("Interval"), 20, 0.01m, 10m, (decimal)layer.FillSettings.IntervalMm, 2);
                NumericUpDown angle = AddNumeric(form, LayerText.Get("Angle"), 55, 0, 359, (decimal)layer.FillSettings.AngleDegrees, 1);
                CheckBox bidirectional = new CheckBox { Left = 20, Top = 92, Width = 180, Text = LayerText.Get("Bidirectional"), Checked = layer.FillSettings.Bidirectional, ForeColor = form.ForeColor };
                CheckBox outside = new CheckBox { Left = 20, Top = 118, Width = 220, Text = LayerText.Get("OutsideInside"), Checked = layer.FillSettings.OutsideToInside, ForeColor = form.ForeColor };
                Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 155, Top = 150, Width = 70 }; Button cancel = new Button { Text = LayerText.Get("Cancel"), DialogResult = DialogResult.Cancel, Left = 235, Top = 150, Width = 70 };
                form.Controls.AddRange(new Control[] { bidirectional, outside, ok, cancel }); form.AcceptButton = ok; form.CancelButton = cancel;
                if (form.ShowDialog(this) == DialogResult.OK) { layer.FillSettings.IntervalMm = (double)interval.Value; layer.FillSettings.AngleDegrees = (double)angle.Value; layer.FillSettings.Bidirectional = bidirectional.Checked; layer.FillSettings.OutsideToInside = outside.Checked; core.LayerController.Changed(); }
            }
        }

        private NumericUpDown AddNumeric(Form form, string label, int top, decimal min, decimal max, decimal value, int decimals)
        {
            form.Controls.Add(new Label { Left = 20, Top = top + 3, Width = 140, Text = label, ForeColor = form.ForeColor });
            NumericUpDown control = new NumericUpDown { Left = 175, Top = top, Width = 125, Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = decimals == 1 ? 1 : 0.01m, Value = Math.Max(min, Math.Min(max, value)) };
            form.Controls.Add(control); return control;
        }

        private void ToggleCollapsed(object sender, EventArgs e) { IsCollapsed = !IsCollapsed; grid.Visible = status.Visible = !IsCollapsed; CollapseChanged?.Invoke(IsCollapsed); ((ToolStripButton)sender).Text = IsCollapsed ? ">" : "<"; }
    }
}
