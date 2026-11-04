//Copyright (c) 2016-2021 Diego Settimi - https://github.com/arkypita/

// This program is free software; you can redistribute it and/or modify  it under the terms of the GPLv3 General Public License as published by  the Free Software Foundation; either version 3 of the License, or (at  your option) any later version.
// This program is distributed in the hope that it will be useful, but  WITHOUT ANY WARRANTY; without even the implied warranty of  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GPLv3  General Public License for more details.
// You should have received a copy of the GPLv3 General Public License  along with this program; if not, write to the Free Software  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307,  USA. using System;

using LaserGRBL.Icons;
using LaserGRBL.UserControls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using LaserGRBL.Layering;

namespace LaserGRBL
{
    /// <summary>
    /// Description of PreviewForm.
    /// </summary>
    public partial class PreviewForm : System.Windows.Forms.UserControl
	{
		private GrblCore mCore;
		private LayerManagerControl LayerManager;
		private SplitContainer WorkspaceSplit;
		private int ExpandedLayerWidth = 430;
		private bool SplitInitialized;
		private Panel CanvasHost;
		private ToolStrip SizeToolbar;
		private ToolStripTextBox WidthBox;
		private ToolStripTextBox HeightBox;
		private ToolStripButton LockAspectButton;
		private ToolStripButton ObjectModeButton;
		private ToolStripButton DeleteObjectsButton;
        private ToolStripButton CopyButton, PasteButton, DuplicateButton;
		private readonly List<VectorSelection> SelectedObjects = new List<VectorSelection>();
		private Dictionary<VectorSelection, VectorPath> ObjectDragPaths;
		private bool MovingObjects;
		private Panel ResizeHandle;
		private List<LaserLayer> SelectedLayers = new List<LaserLayer>();
		private RectangleF DragStartBounds;
		private bool ResizingLayer;
		private bool MovingLayers;
		private PointF DragStartPoint;
		private Dictionary<Guid, LayerTransformState> DragTransforms = new Dictionary<Guid, LayerTransformState>();
		private long LastPreviewCompile;
		public IGrblPanel GrblPanel { private set; get; } = null;

		public PreviewForm()
		{
			InitializeComponent();
			LayerManager = new LayerManagerControl();
			WorkspaceSplit = new SplitContainer
			{
				Dock = DockStyle.Fill,
				Orientation = Orientation.Vertical,
				FixedPanel = FixedPanel.Panel2,
				Panel1MinSize = 200,
				Panel2MinSize = 34,
				SplitterWidth = 6
			};
			tableLayoutPanel1.ColumnCount = 1;
			tableLayoutPanel1.ColumnStyles.Clear();
			tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			tableLayoutPanel1.Controls.Add(WorkspaceSplit, 0, 0);
			WorkspaceSplit.Panel2.Controls.Add(LayerManager);
			CanvasHost = new Panel { Dock = DockStyle.Fill };
			WorkspaceSplit.Panel1.Controls.Add(CanvasHost);
			BuildSizeToolbar();
			WorkspaceSplit.SplitterMoved += (s, e) =>
			{
				if (SplitInitialized && !LayerManager.IsCollapsed)
					ExpandedLayerWidth = WorkspaceSplit.Panel2.Width;
			};
			LayerManager.CollapseChanged += SetLayersCollapsed;
			LayerManager.SelectedLayersChanged += SetSelectedLayers;
            LayerManager.CopiesInserted += SelectCopies;
            CreatePanel();
            CustomButtonArea.OrderChanged += CustomButtonArea_OrderChanged;
			IconsMgr.PrepareButton(BtnReset, "custom-reset");
            IconsMgr.PrepareButton(BtnUnlock, "custom-unlock");
            IconsMgr.PrepareButton(BtnHoming, "custom-homing");
            IconsMgr.PrepareButton(BtnZeroing, "custom-zeroing");
            IconsMgr.PrepareButton(BtnResume, "custom-resume");
            IconsMgr.PrepareButton(BtnStop, "custom-stop");
            if (IconsMgr.LegacyIcons)
            {
                BtnReset.BorderStyle = BorderStyle.FixedSingle;
                BtnUnlock.BorderStyle = BorderStyle.FixedSingle;
                BtnHoming.BorderStyle = BorderStyle.FixedSingle;
                BtnZeroing.BorderStyle = BorderStyle.FixedSingle;
                BtnResume.BorderStyle = BorderStyle.FixedSingle;
                BtnStop.BorderStyle = BorderStyle.FixedSingle;
            }
            else
            {
                BtnReset.BorderStyle = BorderStyle.None;
                BtnUnlock.BorderStyle = BorderStyle.None;
                BtnHoming.BorderStyle = BorderStyle.None;
                BtnZeroing.BorderStyle = BorderStyle.None;
                BtnResume.BorderStyle = BorderStyle.None;
                BtnStop.BorderStyle = BorderStyle.None;
                BtnReset.RoundedBorders = true;
                BtnUnlock.RoundedBorders = true;
                BtnHoming.RoundedBorders = true;
                BtnZeroing.RoundedBorders = true;
                BtnResume.RoundedBorders = true;
                BtnStop.RoundedBorders = true;
            }
        }

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			InitializeSplitter();
		}

		private void InitializeSplitter()
		{
			if (SplitInitialized || WorkspaceSplit.Width <= WorkspaceSplit.Panel1MinSize + WorkspaceSplit.SplitterWidth + 34) return;
			SetLayerPanelWidth(Math.Min(ExpandedLayerWidth, WorkspaceSplit.Width - WorkspaceSplit.Panel1MinSize - WorkspaceSplit.SplitterWidth));
			SplitInitialized = true;
		}

		private void SetLayersCollapsed(bool collapsed)
		{
			if (!SplitInitialized) InitializeSplitter();
			if (collapsed)
			{
				ExpandedLayerWidth = Math.Max(280, WorkspaceSplit.Panel2.Width);
				WorkspaceSplit.Panel2MinSize = 34;
				SetLayerPanelWidth(34);
				WorkspaceSplit.IsSplitterFixed = true;
			}
			else
			{
				WorkspaceSplit.IsSplitterFixed = false;
				WorkspaceSplit.Panel2MinSize = 280;
				SetLayerPanelWidth(ExpandedLayerWidth);
			}
		}

		private void SetLayerPanelWidth(int requestedWidth)
		{
			int maximum = Math.Max(WorkspaceSplit.Panel2MinSize, WorkspaceSplit.Width - WorkspaceSplit.Panel1MinSize - WorkspaceSplit.SplitterWidth);
			int width = Math.Max(WorkspaceSplit.Panel2MinSize, Math.Min(maximum, requestedWidth));
			WorkspaceSplit.SplitterDistance = Math.Max(WorkspaceSplit.Panel1MinSize, WorkspaceSplit.Width - WorkspaceSplit.SplitterWidth - width);
		}

        private void CreatePanel()
        {
			if (GrblPanel != null)
            {
				(GrblPanel as Control).Parent?.Controls.Remove(GrblPanel as Control);
				(GrblPanel as Control).Dispose();
            }
            if (Settings.RequestedGraphicMode == Settings.GraphicMode.GDI)
                GrblPanel = new GrblPanel();
            else
                GrblPanel = new GrblPanel3D();

			if (mCore != null) GrblPanel.SetCore(mCore);
            (GrblPanel as Control).Dock = DockStyle.Fill;
			CanvasHost.Controls.Add(GrblPanel as Control);
			(GrblPanel as Control).BringToFront();
			SizeToolbar.BringToFront();
			CreateResizeHandle();
			Control canvas = GrblPanel as Control;
			canvas.MouseDown += CanvasMouseDown;
			canvas.MouseMove += CanvasMouseMove;
			canvas.MouseUp += CanvasMouseUp;
			RefreshSelectionEditor();
        }

		private void BuildSizeToolbar()
		{
			SizeToolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, BackColor = ColorScheme.FormBackColor, ForeColor = ColorScheme.FormForeColor };
			WidthBox = new ToolStripTextBox { Width = 64, ToolTipText = LayerText.Get("WidthTooltip") };
			HeightBox = new ToolStripTextBox { Width = 64, ToolTipText = LayerText.Get("HeightTooltip") };
			LockAspectButton = new ToolStripButton(LayerText.Get("LinkSize")) { Checked = true, CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = LayerText.Get("KeepAspect") };
			SizeToolbar.Items.Add(new ToolStripLabel(LayerText.Get("Width"))); SizeToolbar.Items.Add(WidthBox); SizeToolbar.Items.Add(new ToolStripLabel("mm"));
			SizeToolbar.Items.Add(new ToolStripSeparator()); SizeToolbar.Items.Add(new ToolStripLabel(LayerText.Get("Height"))); SizeToolbar.Items.Add(HeightBox); SizeToolbar.Items.Add(new ToolStripLabel("mm"));
			SizeToolbar.Items.Add(new ToolStripSeparator()); SizeToolbar.Items.Add(LockAspectButton);
			ObjectModeButton = new ToolStripButton(LayerText.Get("Objects")) { CheckOnClick = true, ToolTipText = LayerText.Get("ObjectsTooltip") };
			ToolStripButton layersButton = new ToolStripButton(LayerText.Get("Layers")) { Checked = true };
			layersButton.Click += (s, e) => { ObjectModeButton.Checked = false; };
			ObjectModeButton.CheckedChanged += (s, e) => { layersButton.Checked = !ObjectModeButton.Checked; SelectedObjects.Clear(); RefreshSelectionEditor(); };
			SizeToolbar.Items.Insert(0, new ToolStripSeparator());
			SizeToolbar.Items.Insert(0, ObjectModeButton); SizeToolbar.Items.Insert(0, layersButton);
			DeleteObjectsButton = new ToolStripButton(LayerText.Get("DeleteObjects"));
			DeleteObjectsButton.Click += (s, e) => DeleteSelectedObjects(); SizeToolbar.Items.Add(DeleteObjectsButton);
			CopyButton = new ToolStripButton(LayerText.Get("Copy")) { ToolTipText = "Ctrl+C" };
            PasteButton = new ToolStripButton(LayerText.Get("Paste")) { ToolTipText = "Ctrl+V" };
            DuplicateButton = new ToolStripButton(LayerText.Get("Duplicate")) { ToolTipText = "Ctrl+D" };
            CopyButton.Click += (s, e) => CopyCanvasSelection();
            PasteButton.Click += (s, e) => PasteCanvasSelection(false);
            DuplicateButton.Click += (s, e) => PasteCanvasSelection(true);
            SizeToolbar.Items.AddRange(new ToolStripItem[] { CopyButton, PasteButton, DuplicateButton });
            WidthBox.KeyDown += SizeBoxKeyDown; HeightBox.KeyDown += SizeBoxKeyDown;
			WidthBox.Leave += (s, e) => ApplyTypedSize(true); HeightBox.Leave += (s, e) => ApplyTypedSize(false);
			CanvasHost.Controls.Add(SizeToolbar);
		}

		private void CreateResizeHandle()
		{
			if (ResizeHandle != null) ResizeHandle.Dispose();
			ResizeHandle = new Panel { Size = new Size(12, 12), BackColor = Color.DodgerBlue, Cursor = Cursors.SizeNWSE, Visible = false };
			ResizeHandle.MouseDown += ResizeHandleMouseDown;
			ResizeHandle.MouseMove += ResizeHandleMouseMove;
			ResizeHandle.MouseUp += ResizeHandleMouseUp;
			(GrblPanel as Control).Controls.Add(ResizeHandle);
			ResizeHandle.BringToFront();
			(GrblPanel as Control).Resize += (s, e) => PositionResizeHandle();
		}

		private void SetSelectedLayers(IList<LaserLayer> layers)
		{
			SelectedLayers = layers == null ? new List<LaserLayer>() : layers.Where(layer => layer != null).ToList();
			RefreshSelectionEditor();
		}

		private void RefreshSelectionEditor()
		{
			RectangleF bounds = RectangleF.Empty;
			bool layered = mCore != null && !mCore.JobEditingLocked && mCore.LayerController.IsLayered;
			GrblPanel.SetLayerEditing(layered);
			bool objects = ObjectModeButton.Checked;
			SelectedObjects.RemoveAll(item => !item.IsCurrent(mCore == null ? null : mCore.LayerController.Document));
			DeleteObjectsButton.Enabled = layered && objects && SelectedObjects.Count > 0;
			GrblPanel.SetSelectionContours(objects ? SelectedObjects.Select(item => LayerGeometry.Transform(item.Path, item.Layer)) : Enumerable.Empty<VectorPath>());
			bool enabled = layered && !objects && LayerGeometry.TryGetBounds(SelectedLayers, out bounds);
			SizeToolbar.Enabled = mCore != null && mCore.LayerController.IsLayered;
            bool hasSelection = objects ? SelectedObjects.Count > 0 : SelectedLayers.Count > 0;
            CopyButton.Enabled = hasSelection;
            PasteButton.Enabled = layered && mCore.LayerController.Clipboard.HasContent;
            DuplicateButton.Enabled = layered && hasSelection;
			WidthBox.Enabled = HeightBox.Enabled = LockAspectButton.Enabled = enabled;
			if (objects && SelectedObjects.Count > 0)
			{
				bounds = SelectedObjects[0].Bounds;
				foreach (VectorSelection item in SelectedObjects.Skip(1)) bounds = RectangleF.Union(bounds, item.Bounds);
				GrblPanel.SetSelectionBounds(bounds);
				WidthBox.Text = bounds.Width.ToString("0.###", CultureInfo.CurrentCulture); HeightBox.Text = bounds.Height.ToString("0.###", CultureInfo.CurrentCulture);
				if (ResizeHandle != null) ResizeHandle.Visible = false;
				return;
			}
			if (!enabled)
			{
				WidthBox.Text = HeightBox.Text = string.Empty;
				if (GrblPanel != null) GrblPanel.SetSelectionBounds(null);
				if (ResizeHandle != null) ResizeHandle.Visible = false;
				return;
			}
			if (!WidthBox.Focused) WidthBox.Text = bounds.Width.ToString("0.###", CultureInfo.CurrentCulture);
			if (!HeightBox.Focused) HeightBox.Text = bounds.Height.ToString("0.###", CultureInfo.CurrentCulture);
			GrblPanel.SetSelectionBounds(bounds);
			PositionResizeHandle();
		}

		private void PositionResizeHandle()
		{
			RectangleF bounds;
			if (ResizeHandle == null || GrblPanel == null || ObjectModeButton.Checked || mCore == null || mCore.JobEditingLocked || !LayerGeometry.TryGetBounds(SelectedLayers, out bounds)) { if (ResizeHandle != null) ResizeHandle.Visible = false; return; }
			PointF point = GrblPanel.MachineToClient(new PointF(bounds.Right, bounds.Top));
			ResizeHandle.Left = (int)Math.Round(point.X) - ResizeHandle.Width / 2;
			ResizeHandle.Top = (int)Math.Round(point.Y) - ResizeHandle.Height / 2;
			ResizeHandle.Visible = true;
			ResizeHandle.BringToFront();
		}

		private void ResizeHandleMouseDown(object sender, MouseEventArgs e)
		{
			if (mCore == null || mCore.JobEditingLocked) return;
			if (e.Button != MouseButtons.Left || !LayerGeometry.TryGetBounds(SelectedLayers, out DragStartBounds)) return;
			CaptureTransforms();
			ResizingLayer = true; ResizeHandle.Capture = true;
		}

		private void ResizeHandleMouseMove(object sender, MouseEventArgs e)
		{
			if (mCore == null || mCore.JobEditingLocked) return;
			if (!ResizingLayer || e.Button != MouseButtons.Left) return;
			Point screen = ResizeHandle.PointToScreen(e.Location);
			Point client = (GrblPanel as Control).PointToClient(screen);
			PointF machine = GrblPanel.ClientToMachine(client);
			double width = Math.Max(0.01, machine.X - DragStartBounds.Left);
			double height = Math.Max(0.01, DragStartBounds.Bottom - machine.Y);
			if (LockAspectButton.Checked)
			{
				double ratio = Math.Max(width / DragStartBounds.Width, height / DragStartBounds.Height);
				width = DragStartBounds.Width * ratio; height = DragStartBounds.Height * ratio;
			}
			ApplyResize(width, height, true, true);
		}

		private void ResizeHandleMouseUp(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left) return;
			ResizingLayer = false; ResizeHandle.Capture = false; CommitCanvasTransform();
		}

		private void SizeBoxKeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode != Keys.Enter) return;
			ApplyTypedSize(sender == WidthBox); e.Handled = true; e.SuppressKeyPress = true;
		}

		private void ApplyTypedSize(bool widthChanged)
		{
			if (mCore == null || mCore.JobEditingLocked) return;
			if (ObjectModeButton.Checked) return;
			RectangleF bounds; double value;
			ToolStripTextBox source = widthChanged ? WidthBox : HeightBox;
			if (!LayerGeometry.TryGetBounds(SelectedLayers, out bounds) || !TryReadDimension(source.Text, out value) || value <= 0) { RefreshSelectionEditor(); return; }
			double width = widthChanged ? value : bounds.Width;
			double height = widthChanged ? bounds.Height : value;
			if (LockAspectButton.Checked)
			{
				if (widthChanged) height = bounds.Height * value / bounds.Width;
				else width = bounds.Width * value / bounds.Height;
			}
			DragStartBounds = bounds;
			CaptureTransforms();
			ApplyResize(width, height, false, false);
			CommitCanvasTransform();
		}

		private static bool TryReadDimension(string text, out double value)
		{
			return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		}

		private void CaptureTransforms()
		{
			DragTransforms = SelectedLayers.ToDictionary(layer => layer.Id, layer => new LayerTransformState(layer));
		}

		private void ApplyResize(double width, double height, bool preserveMaximumY, bool livePreview)
		{
			if (mCore == null || DragStartBounds.Width <= 0 || DragStartBounds.Height <= 0) return;
			double ratioX = width / DragStartBounds.Width;
			double ratioY = height / DragStartBounds.Height;
			double anchorX = DragStartBounds.Left;
			double anchorY = preserveMaximumY ? DragStartBounds.Bottom : DragStartBounds.Top;
			foreach (LaserLayer layer in SelectedLayers)
			{
				LayerTransformState state;
				if (!DragTransforms.TryGetValue(layer.Id, out state)) continue;
				layer.ScaleX = state.ScaleX * ratioX;
				layer.ScaleY = state.ScaleY * ratioY;
				layer.TranslateX = anchorX + (state.TranslateX - anchorX) * ratioX;
				layer.TranslateY = anchorY + (state.TranslateY - anchorY) * ratioY;
			}
			RefreshSelectionEditor();
			if (livePreview) RequestLivePreview();
		}

		private void CanvasMouseDown(object sender, MouseEventArgs e)
		{
			if (mCore == null || mCore.JobEditingLocked) return;
			if (e.Button != MouseButtons.Left || mCore == null || !mCore.LayerController.IsLayered) return;
			(sender as Control).Focus();
			PointF machine = GrblPanel.ClientToMachine(e.Location);
			if (ObjectModeButton.Checked)
			{
				VectorSelection objectHit = LayerGeometry.FindContour(mCore.LayerController.Document, e.Location, GrblPanel.MachineToClient, 6);
				bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
				VectorSelection existing = objectHit == null ? null : SelectedObjects.FirstOrDefault(item => item.Path == objectHit.Path && item.Layer == objectHit.Layer);
				if (!ctrl && existing == null) SelectedObjects.Clear();
				if (objectHit != null)
				{
					if (ctrl && existing != null) SelectedObjects.Remove(existing);
					else if (existing == null) SelectedObjects.Add(objectHit);
					MovingObjects = !ctrl || existing == null;
					DragStartPoint = machine;
					ObjectDragPaths = SelectedObjects.ToDictionary(item => item, item => item.Path.Clone());
					(sender as Control).Capture = MovingObjects;
				}
				RefreshSelectionEditor();
				return;
			}
			LaserLayer hit = FindLayerAt(machine);
			bool control = (ModifierKeys & Keys.Control) == Keys.Control;
			if (hit == null)
			{
				if (!control) LayerManager.SelectLayers(new LaserLayer[0]);
				return;
			}
			List<LaserLayer> selection = new List<LaserLayer>(SelectedLayers);
			if (control)
			{
				if (selection.Contains(hit)) selection.Remove(hit); else selection.Add(hit);
			}
			else { selection.Clear(); selection.Add(hit); }
			LayerManager.SelectLayers(selection);
			if (!selection.Contains(hit)) return;
			MovingLayers = true;
			DragStartPoint = machine;
			CaptureTransforms();
			(sender as Control).Capture = true;
		}

		private void CanvasMouseMove(object sender, MouseEventArgs e)
		{
			if (mCore == null || mCore.JobEditingLocked) return;
			if (MovingObjects && e.Button == MouseButtons.Left)
			{
				PointF current = GrblPanel.ClientToMachine(e.Location);
				foreach (var item in ObjectDragPaths) item.Key.MoveFrom(item.Value, current.X - DragStartPoint.X, current.Y - DragStartPoint.Y);
				RefreshSelectionEditor(); RequestLivePreview(); return;
			}
			if (!MovingLayers || e.Button != MouseButtons.Left) return;
			PointF machine = GrblPanel.ClientToMachine(e.Location);
			double dx = machine.X - DragStartPoint.X;
			double dy = machine.Y - DragStartPoint.Y;
			foreach (LaserLayer layer in SelectedLayers)
			{
				LayerTransformState state;
				if (!DragTransforms.TryGetValue(layer.Id, out state)) continue;
				layer.TranslateX = state.TranslateX + dx;
				layer.TranslateY = state.TranslateY + dy;
			}
			RefreshSelectionEditor();
			RequestLivePreview();
		}

		private void CanvasMouseUp(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left && MovingObjects)
			{
				MovingObjects = false; (sender as Control).Capture = false; CommitCanvasTransform(); return;
			}
			if (e.Button != MouseButtons.Left || !MovingLayers) return;
			MovingLayers = false;
			(sender as Control).Capture = false;
			CommitCanvasTransform();
		}

		private LaserLayer FindLayerAt(PointF point)
		{
			if (mCore == null || mCore.LayerController.Document == null) return null;
			return mCore.LayerController.Document.Layers.Where(layer => layer.Visible).Select(layer =>
			{
				RectangleF bounds; return new { Layer = layer, HasBounds = LayerGeometry.TryGetBounds(layer, out bounds), Bounds = bounds };
			}).Where(item => item.HasBounds && item.Bounds.Contains(point)).OrderBy(item => item.Bounds.Width * item.Bounds.Height).Select(item => item.Layer).FirstOrDefault();
		}

		private void RequestLivePreview()
		{
			long now = System.Diagnostics.Stopwatch.GetTimestamp();
			long interval = Math.Max(1, System.Diagnostics.Stopwatch.Frequency / 20);
			if (now - LastPreviewCompile < interval) return;
			LastPreviewCompile = now;
			mCore.LayerController.RequestCompile(true, false);
		}

		private void CommitCanvasTransform()
		{
			if (mCore == null) return;
			mCore.LayerController.Changed(false);
			mCore.LayerController.RequestCompile(true);
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			bool canvasFocused = (GrblPanel as Control).ContainsFocus;
            bool editingText = WidthBox.Focused || HeightBox.Focused || LayerManager.IsEditingText;
            if (!editingText && mCore != null && mCore.LayerController.IsLayered && (canvasFocused || LayerManager.ContainsFocus || SizeToolbar.ContainsFocus))
            {
                if (keyData == (Keys.Control | Keys.C)) { if (LayerManager.ContainsFocus) mCore.LayerController.CopyLayers(SelectedLayers); else CopyCanvasSelection(); RefreshSelectionEditor(); return true; }
                if (keyData == (Keys.Control | Keys.V)) { PasteCanvasSelection(false); return true; }
                if (keyData == (Keys.Control | Keys.D)) { PasteCanvasSelection(true, LayerManager.ContainsFocus); return true; }
            }
			if (keyData == Keys.Escape && canvasFocused && ObjectModeButton.Checked) { SelectedObjects.Clear(); RefreshSelectionEditor(); return true; }
			if (keyData == Keys.Delete && canvasFocused && ObjectModeButton.Checked) { DeleteSelectedObjects(); return true; }
			if (keyData == (Keys.Control | Keys.A) && mCore != null && mCore.LayerController.IsLayered && !WidthBox.Focused && !HeightBox.Focused && !LayerManager.IsEditingText)
			{
				if (ObjectModeButton.Checked && canvasFocused)
				{
					SelectedObjects.Clear();
					foreach (LaserLayer layer in mCore.LayerController.Document.Layers.Where(l => l.Visible && l.ContentKind == LayerContentKind.Vector))
						foreach (VectorItem item in layer.VectorItems)
							foreach (VectorPath path in item.Paths.Where(p => p.Points.Count > 1)) SelectedObjects.Add(new VectorSelection(layer, item, path));
					RefreshSelectionEditor(); return true;
				}
				LayerManager.SelectAllLayers();
				return true;
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}

		private void CopyCanvasSelection()
        {
            if (mCore == null || !mCore.LayerController.IsLayered) return;
            if (ObjectModeButton.Checked) mCore.LayerController.CopyObjects(SelectedObjects);
            else mCore.LayerController.CopyLayers(SelectedLayers);
            RefreshSelectionEditor();
        }
        private void PasteCanvasSelection(bool duplicate, bool layersOnly = false)
        {
            if (mCore == null || mCore.JobEditingLocked || !mCore.LayerController.IsLayered) return;
            bool objects = duplicate ? ObjectModeButton.Checked && !layersOnly : mCore.LayerController.Clipboard.ContainsObjects;
            IList<LaserLayer> copies;
            if (!duplicate) copies = mCore.LayerController.Paste();
            else if (objects) copies = mCore.LayerController.DuplicateObjects(SelectedObjects);
            else copies = mCore.LayerController.DuplicateLayers(SelectedLayers);
            SelectCopies(objects, copies);
        }
        private void SelectCopies(bool objects, IList<LaserLayer> copies)
        {
            if (copies.Count == 0) return;
            ObjectModeButton.Checked = objects;
            LayerManager.SelectLayers(copies);
            SelectedObjects.Clear();
            if (objects)
                foreach (LaserLayer layer in copies)
                    foreach (VectorItem item in layer.VectorItems)
                        foreach (VectorPath path in item.Paths) SelectedObjects.Add(new VectorSelection(layer, item, path));
            RefreshSelectionEditor();
        }
        private void DeleteSelectedObjects()
		{
			if (mCore == null || mCore.JobEditingLocked) return;
			if (!ObjectModeButton.Checked || mCore == null || !mCore.LayerController.IsLayered || SelectedObjects.Count == 0) return;
			foreach (VectorSelection item in SelectedObjects.Where(item => item.IsCurrent(mCore.LayerController.Document))) item.Delete();
			SelectedObjects.Clear();
			mCore.LayerController.Changed(); RefreshSelectionEditor();
		}

		private sealed class LayerTransformState
		{
			public readonly double ScaleX, ScaleY, TranslateX, TranslateY;
			public LayerTransformState(LaserLayer layer) { ScaleX = layer.ScaleX; ScaleY = layer.ScaleY; TranslateX = layer.TranslateX; TranslateY = layer.TranslateY; }
		}

        private void CustomButtonArea_OrderChanged(int oldindex, int newindex)
		{
			CustomButtons.Reorder(oldindex, newindex);
			RefreshCustomButtons();
		}

		public void SetCore(GrblCore core)
		{
			mCore = core;
            GrblPanel.SetCore(core);
			LayerManager.SetCore(core);
			core.LayerController.DocumentChanged += RefreshSelectionEditor;

            BtnUnlock.Visible = mCore.Type == Firmware.Grbl;

            RefreshCustomButtons();
			TimerUpdate();
		}

		public void TimerUpdate()
		{
            LayerManager.UpdateJobLock();
            RefreshSelectionEditor();
            GrblPanel.TimerUpdate();
			PositionResizeHandle();
			SuspendLayout();
			BtnReset.Enabled = mCore.CanResetGrbl;
			BtnHoming.Visible = GrblCore.Configuration.HomingEnabled;
			BtnHoming.Enabled = mCore.CanDoHoming;
			BtnUnlock.Enabled = mCore.CanUnlock;
			BtnStop.Enabled = mCore.CanFeedHold;
			BtnResume.Enabled = mCore.CanResumeHold;
			BtnZeroing.Enabled = mCore.CanDoZeroing;
			foreach (CustomButtonIB ib in CustomButtonArea.Controls)
			{
                ib.RefreshEnabled();
            }
				

			ResumeLayout();
		}

		void BtnGoHomeClick(object sender, EventArgs e)
		{
			mCore.GrblHoming();
		}
		void BtnResetClick(object sender, EventArgs e)
		{
			mCore.GrblReset();
		}
		void BtnStopClick(object sender, EventArgs e)
		{
			mCore.FeedHold(false);
		}
		void BtnResumeClick(object sender, EventArgs e)
		{
			mCore.CycleStartResume(false);
		}

		private void BtnUnlockClick(object sender, EventArgs e)
		{
			mCore.GrblUnlock();
		}

		private void addCustomButtonToolStripMenuItem_Click(object sender, EventArgs e)
		{
			CustomButtonForm.CreateAndShowDialog(ParentForm);
			RefreshCustomButtons();
		}

		private void RefreshCustomButtons()
		{
			CustomButtonArea.Controls.Clear();
			foreach (CustomButton cb in CustomButtons.Buttons)
			{
				CustomButtonIB ib = new CustomButtonIB(mCore, cb, this);
                if (!IconsMgr.LegacyIcons)
                {
                    switch (ib.Caption)
                    {
                        case "Frame": IconsMgr.PrepareButton(ib, "custom-framing", BtnStop.Size); break;
                        case "Center": IconsMgr.PrepareButton(ib, "custom-center", BtnStop.Size); break;
                        case "Corner": IconsMgr.PrepareButton(ib, "custom-corner", BtnStop.Size); break;
                        case "Focus": IconsMgr.PrepareButton(ib, "custom-focus", BtnStop.Size); break;
                        case "Blink": IconsMgr.PrepareButton(ib, "custom-blink", BtnStop.Size); break;
                    }
                    string mdiIconCode = $"mdi-{ib.Caption}";
                    if (IconsMgr.IconsLoader.Contains(mdiIconCode))
                    {
                        IconsMgr.PrepareButton(ib, mdiIconCode, BtnStop.Size);
                    }
                }
                CustomButtonArea.Controls.Add(ib);
			}

		}

        public List<CustomButtonIB> CustomImageButtons
        {
            get
            {
                List<CustomButtonIB> rv = new List<CustomButtonIB>();
                foreach (Control c in CustomButtonArea.Controls)
                    if (c is CustomButtonIB)
                        rv.Add(c as CustomButtonIB);
                return rv;
            }
        }
        public class CustomButtonIB : UserControls.ImageButton
        {
            private GrblCore Core;
            private LaserGRBL.CustomButton cb;
            PreviewForm form;
            private ToolTip tt;
            private ContextMenuStrip cms;
            private bool mDrawDisabled = true;

            public CustomButtonIB(GrblCore Core, LaserGRBL.CustomButton cb, PreviewForm form)
            {
                // TODO: Complete member initialization
                this.Core = Core;
                this.cb = cb;
                this.form = form;
                this.tt = new ToolTip();

                Image = cb.Image;
                Tag = cb;

                //ContextMenuStrip = MNRemEditCB;
                SizingMode = UserControls.ImageButton.SizingModes.FixedSize;
                BorderStyle = IconsMgr.LegacyIcons ? BorderStyle.FixedSingle : BorderStyle.None;
                RoundedBorders = true;

                Size = new Size(49, 49);
                Margin = new Padding(2);
                this.Caption = cb.Caption;
                tt.SetToolTip(this, cb.ToolTip);


                cms = new ContextMenuStrip();
                cms.Items.Add(Strings.CustomButtonRemove, null, RemoveButton_Click);
                cms.Items.Add(Strings.CustomButtonEdit, null, EditButton_Click);

                ContextMenuStrip = cms;
            }

            private bool PositionUnlocked
            {
                get
                {
                    MyFlowPanel panel = Parent as MyFlowPanel;
                    return panel != null && panel.ButtonPositionUnlocked;
                }
            }


            protected override void Dispose(bool disposing)
            {
                tt.SetToolTip(this, null);
                base.Dispose(disposing);
            }

            public CustomButton CustomButton
            { get { return Tag as CustomButton; } }

            internal void RefreshEnabled()
            {
                bool disabled = !CustomButton.EnabledNow(Core) || PositionUnlocked;
                if (mDrawDisabled != disabled)
                {
                    mDrawDisabled = disabled;
                    Refresh();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);


                Rectangle r = new Rectangle(0, 0, e.ClipRectangle.Width - 1, e.ClipRectangle.Height - 1);

                if (PositionUnlocked)
                {
                    using (Pen p = new Pen(Color.FromArgb(150, 135, 206, 250)))
                    {
                        e.Graphics.DrawRectangle(p, r);
                        for (int i = 0; i < 2 * Math.Max(r.Width, r.Height); i += 5)
                            e.Graphics.DrawLine(p, i, 0, 0, i);
                    }
                }
            }

            protected override bool DrawDisabled()
            {
                return mDrawDisabled;
            }

            protected override void OnClick(EventArgs e)
            { PerformClick(e); }

            private bool mEmulateMouseInside;
            public bool EmulateMouseInside
            {
                get { return mEmulateMouseInside; }
                set { mEmulateMouseInside = value; Invalidate(); }
            }

            public override bool IsMouseInside()
            {
                return EmulateMouseInside || base.IsMouseInside();
            }

            private bool on;
            public void PerformClick(EventArgs e)
            {
                if (((MouseEventArgs)e).Button != MouseButtons.Left)
                    return;

                if (mDrawDisabled || !CustomButton.EnabledNow(Core))
                    return;

                if (cb.ButtonType == CustomButton.ButtonTypes.Button && !PositionUnlocked)
                    Core.ExecuteCustomCode(cb.GCode);

                if (cb.ButtonType == CustomButton.ButtonTypes.TwoStateButton && !PositionUnlocked)
                {
                    on = !on;
                    Core.ExecuteCustomCode(on ? cb.GCode : cb.GCode2);
                    BackColor = on ? ColorScheme.PressedButtons : Parent.BackColor;
                }

                base.OnClick(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                _mX = e.X;
                _mY = e.Y;
                this._isDragging = false;

                PerformMouseDown(e);
            }

            public void PerformMouseDown(MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left)
                    return;

                if (mDrawDisabled || !CustomButton.EnabledNow(Core))
                    return;

                if (cb.ButtonType == CustomButton.ButtonTypes.PushButton && !PositionUnlocked)
                {
                    Core.ExecuteCustomCode(cb.GCode);
                    BackColor = ColorScheme.PressedButtons;
                }

                base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                _isDragging = false;
                PerformMouseUp(e);
            }

            public void PerformMouseUp(MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left)
                    return;

                if (mDrawDisabled || !CustomButton.EnabledNow(Core))
                    return;

                if (cb.ButtonType == CustomButton.ButtonTypes.PushButton && !PositionUnlocked)
                {
                    Core.ExecuteCustomCode(cb.GCode2);
                    BackColor = Parent.BackColor;
                }

                base.OnMouseUp(e);
            }

            private void RemoveButton_Click(object sender, EventArgs e)
            {
                if (MessageBox.Show(Strings.BoxDeleteCustomButtonText, Strings.BoxDeleteCustomButtonTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                {
                    CustomButtons.Remove(CustomButton);
                    CustomButtons.SaveFile();
                    form.RefreshCustomButtons();
                }
            }

            private void EditButton_Click(object sender, EventArgs e)
            {
                CustomButtonForm.CreateAndShowDialog(ParentForm, CustomButton);
                form.RefreshCustomButtons();
            }


            #region DragDrop


            //Check radius for begin drag n drop
            //public bool AllowDrag { get; set; }
            private bool _isDragging = false;
            private int _DDradius = 40;
            private int _mX = 0;
            private int _mY = 0;

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (!_isDragging)
                {
                    // This is a check to see if the mouse is moving while pressed.
                    // Without this, the DragDrop is fired directly when the control is clicked, now you have to drag a few pixels first.
                    if (e.Button == MouseButtons.Left && _DDradius > 0 && PositionUnlocked)
                    {
                        int num1 = _mX - e.X;
                        int num2 = _mY - e.Y;
                        if (((num1 * num1) + (num2 * num2)) > _DDradius)
                        {
                            DoDragDrop(this, DragDropEffects.Move);
                            _isDragging = true;
                            return;
                        }
                    }
                    base.OnMouseMove(e);
                }
            }

            #endregion

        }

        private class MyFlowPanel : FlowLayoutPanel
		{
			public delegate void OrderChangedDlg(int oldindex, int newindex);
			public event OrderChangedDlg OrderChanged;

			public MyFlowPanel()
			{
				AllowDrop = true;
				ResizeRedraw = true;
			}

			protected override void OnControlAdded(ControlEventArgs e)
			{
				base.OnControlAdded(e);
				Invalidate();
			}
			protected override void OnControlRemoved(ControlEventArgs e)
			{
				base.OnControlRemoved(e);
				Invalidate();
			}

			protected override void OnPaintBackground(PaintEventArgs e)
			{
				e.Graphics.Clear(ColorScheme.FormBackColor);
				if (Controls.Count == 0)
				{
					string text = Strings.AddCustomButtonsHint;
					SizeF size = e.Graphics.MeasureString(text, Font);
					e.Graphics.DrawString(text, Font, Brushes.DarkGray, (Width - size.Width) / 2, (Height - size.Height) / 2);
				}
			}

			protected override void OnDragEnter(DragEventArgs drgevent)
			{
				CustomButtonIB btn = (CustomButtonIB)drgevent.Data.GetData(typeof(CustomButtonIB));
				if (btn != null)
					drgevent.Effect = DragDropEffects.Move;
				else
					drgevent.Effect = DragDropEffects.None;

				base.OnDragEnter(drgevent);
			}

			int dragInsertIndex = -1;

			public bool ButtonPositionUnlocked { get; internal set; }

			protected override void OnDragOver(DragEventArgs drgevent)
			{
				MyFlowPanel dst = this;

				Point p = dst.PointToClient(new Point(drgevent.X, drgevent.Y));
				Control item = dst.GetChildAtPoint(p);

				if (item == null) // move the point a bit to the left, if the cursor is between two buttons
				{
					p.X -= 6;
					item = dst.GetChildAtPoint(p);
				}

				if (item != null)
				{
					int currentIndex = dst.Controls.GetChildIndex(item, false);

					Point pRelativeToControl = item.PointToClient(new Point(drgevent.X, drgevent.Y));
					double xInPercent = pRelativeToControl.X / (double)item.Width * 100.0;
					bool isLastItem = false;

					if (xInPercent > 50.0)
					{
						if (++currentIndex >= dst.Controls.Count)
						{
							item = dst.Controls[dst.Controls.Count - 1];
							isLastItem = true;
						}
						else
						{
							item = dst.Controls[currentIndex];
						}
					}

					if (item != null && (dragInsertIndex == -1 || currentIndex != dragInsertIndex))
					{
						using (Graphics g = CreateGraphics())
						{
							g.Clear(this.BackColor);
							dragInsertIndex = currentIndex;
							int xPos = item.Location.X - 3;
							if (isLastItem)
							{
								xPos = item.Location.X + item.Width + 1;
							}
							g.FillRectangle(Brushes.CornflowerBlue, xPos, 2, 2, item.Height);
						}
					}
				}


				base.OnDragOver(drgevent);
			}

			protected override void OnDragDrop(DragEventArgs drgevent)
			{
				CustomButtonIB btn = (CustomButtonIB)drgevent.Data.GetData(typeof(CustomButtonIB));
				MyFlowPanel dst = this;
				MyFlowPanel src = btn?.Parent as MyFlowPanel;

				if (btn != null && src != null && dst != null &&  src == dst)
				{
					drgevent.Effect = DragDropEffects.Move;

					int oldindex = dst.Controls.GetChildIndex(btn);		
					OrderChanged?.Invoke(oldindex, dragInsertIndex);
					dragInsertIndex = -1;
				}
				else
				{
					drgevent.Effect = DragDropEffects.None;
					dragInsertIndex = -1;
				}

				base.OnDragDrop(drgevent);
			}

			protected override void OnDragLeave(EventArgs e)
			{
				dragInsertIndex = -1;
				using (Graphics g = CreateGraphics())
				{
					g.Clear(this.BackColor);
				}

				base.OnDragLeave(e);
			}

		}


		internal void OnColorChange()
		{
            GrblPanel.OnColorChange();
			LayerManager.ApplyTheme();
		}

		private void BtnZeroing_Click(object sender, EventArgs e)
		{
			mCore.SetNewZero();
		}

		private void exportCustomButtonsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			CustomButtons.Export(ParentForm);
		}

		private void importCustomButtonsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			ImportButton();
		}

		public void ImportButton(string filename = null)
		{
			if (CustomButtons.Import(ParentForm, filename))
			{
				RefreshCustomButtons();
				CustomButtons.SaveFile();
			}
		}

		private void MNAddCB_Opening(object sender, CancelEventArgs e)
		{
			exportCustomButtonsToolStripMenuItem.Enabled = CustomButtons.Count > 0;
		}

		private void MnUnlockButtonClick(object sender, EventArgs e)
		{
			MnLockButton.Checked = !MnLockButton.Checked;
			CustomButtonArea.ButtonPositionUnlocked = !MnLockButton.Checked;
			Refresh();
		}

		
	}

}
