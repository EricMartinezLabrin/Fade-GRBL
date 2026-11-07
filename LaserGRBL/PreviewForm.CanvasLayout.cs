using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LaserGRBL.Layering;

namespace LaserGRBL
{
    public partial class PreviewForm
    {
        private ToolStripDropDownButton AlignButton;
        private ToolStripMenuItem AlignToTable, AlignToReference;
        private readonly List<LaserLayer> SelectedRasterItems = new List<LaserLayer>();
        private Guid ReferenceId;
        private bool PointerPending, SelectingRectangle, PointerAdd, ApplyingUnits;
        private Point PointerStart;
        private CanvasSelectionState PointerSelection;
        private CanvasUnit PointerHit;
        private List<CanvasUnit> PointerUnits;
        private bool PointerGeometry;

        private void BuildAlignmentTools()
        {
            AlignButton = new ToolStripDropDownButton(LayerText.Get("Align"));
            AlignToTable = new ToolStripMenuItem(LayerText.Get("AlignToTable")) { Checked = true };
            AlignToReference = new ToolStripMenuItem(LayerText.Get("AlignToReference"));
            AlignToTable.Click += (s, e) => { AlignToTable.Checked = true; AlignToReference.Checked = false; RefreshSelectionEditor(); };
            AlignToReference.Click += (s, e) => { AlignToTable.Checked = false; AlignToReference.Checked = true; RefreshSelectionEditor(); };
            AlignButton.DropDownItems.Add(AlignToTable); AlignButton.DropDownItems.Add(AlignToReference); AlignButton.DropDownItems.Add(new ToolStripSeparator());
            foreach (CanvasAlignmentAction action in Enum.GetValues(typeof(CanvasAlignmentAction)))
            {
                var align = action;
                AlignButton.DropDownItems.Add(LayerText.Get("LayoutAlign" + action), null, (s, e) =>
                { if (mCore != null) { RememberCanvasSelection(); mCore.LayerController.Align(align, AlignToTable.Checked); } });
            }
            EditingToolbar.Items.Add(AlignButton);
        }
        private CanvasSelectionState CurrentCanvasSelection()
        {
            return new CanvasSelectionState { Objects = ObjectModeButton.Checked, ReferenceId = ReferenceId,
                Layers = (ObjectModeButton.Checked ? GroupRasterObjects() : SelectedLayers).Select(l => l.Id).Distinct().ToList(), Paths = SelectedObjects.Select(o => o.Path.Id).Distinct().ToList() };
        }
        private List<CanvasUnit> SelectedCanvasUnits()
        {
            if (mCore == null || !mCore.LayerController.IsLayered) return new List<CanvasUnit>();
            var selection = CurrentCanvasSelection(); return CanvasLayout.Units(mCore.LayerController.Document, selection.Objects, false).Where(u => u.IsSelected(selection)).ToList();
        }
        private void RefreshAlignmentTools(bool editable)
        {
            if (AlignButton == null) return;
            var selected = SelectedCanvasUnits();
            if (!selected.Any(u => u.Id == ReferenceId)) ReferenceId = selected.Count == 0 ? Guid.Empty : selected.Last().Id;
            var reference = selected.FirstOrDefault(u => u.Id == ReferenceId);
            AlignButton.Enabled = editable && selected.Count > 0;
            AlignToReference.Enabled = selected.Count > 1;
            AlignToReference.Text = LayerText.Get("AlignToReference") + (reference == null ? "" : ": " + reference.Name);
            foreach (ToolStripItem item in AlignButton.DropDownItems)
                if (!(item is ToolStripSeparator) && item != AlignToTable && item != AlignToReference) item.Enabled = AlignToTable.Checked || selected.Count > 1;
            GrblPanel.SetReferenceBounds(AlignToReference.Checked && reference != null ? (RectangleF?)reference.Bounds : null);
        }
        private void ApplyCanvasUnits(IEnumerable<CanvasUnit> units, bool objects, Guid reference)
        {
            var chosen = units.ToList(); ApplyingUnits = RestoringSelection = true;
            try
            {
                ObjectModeButton.Checked = objects; ReferenceId = reference;
                SelectedObjects.Clear(); SelectedRasterItems.Clear();
                if (objects)
                {
                    SelectedObjects.AddRange(chosen.SelectMany(u => u.Objects));
                    SelectedRasterItems.AddRange(chosen.SelectMany(u => u.Layers).Where(l => l.ContentKind == LayerContentKind.Raster));
                }
                LayerManager.SelectLayers(chosen.SelectMany(u => u.Layers).Concat(chosen.SelectMany(u => u.Objects).Select(o => o.Layer)).Distinct().ToList());
            }
            finally { ApplyingUnits = RestoringSelection = false; }
            ClearSizeErrors(); RefreshSelectionEditor();
        }
        private void RestorePointerSelection()
        {
            var units = CanvasLayout.Units(mCore.LayerController.Document, PointerSelection.Objects).Where(u => u.IsSelected(PointerSelection));
            ApplyCanvasUnits(units, PointerSelection.Objects, PointerSelection.ReferenceId);
        }
        private void BeginSelectionPointer(MouseEventArgs e)
        {
            PointerStart = e.Location; PointerSelection = CurrentCanvasSelection(); PointerAdd = (ModifierKeys & Keys.Control) != 0;
            var units = CanvasLayout.Units(mCore.LayerController.Document, ObjectModeButton.Checked);
            PointerUnits = units;
            var tolerance = new RectangleF(e.X - 4, e.Y - 4, 8, 8);
            PointerHit = units.Where(u => CanvasLayout.Matches(u, tolerance, true, GrblPanel.MachineToClient))
                .OrderBy(u => u.Bounds.Width * u.Bounds.Height).ThenByDescending(u => units.IndexOf(u)).FirstOrDefault();
            PointerGeometry = PointerHit != null;
            if (PointerHit == null && !ObjectModeButton.Checked)
            {
                var layer = FindLayerAt(GrblPanel.ClientToMachine(e.Location));
                if (layer != null) PointerHit = units.FirstOrDefault(u => u.Layers.Contains(layer));
            }
            PointerPending = true; (GrblPanel as Control).Capture = true;
        }
        private void ApplyPointerClick(bool dragging)
        {
            var units = CanvasLayout.Units(mCore.LayerController.Document, PointerSelection.Objects, false);
            var selected = units.Where(u => u.IsSelected(PointerSelection)).ToList();
            var hit = PointerHit == null ? null : units.FirstOrDefault(u => u.Id == PointerHit.Id);
            if (hit == null) { if (!PointerAdd) selected.Clear(); }
            else if (PointerAdd && selected.Contains(hit) && !dragging) selected.Remove(hit);
            else
            {
                if (!PointerAdd && !selected.Contains(hit)) selected.Clear();
                if (!selected.Contains(hit)) selected.Add(hit);
            }
            var reference = hit != null && selected.Contains(hit) ? hit.Id : selected.Count == 0 ? Guid.Empty : selected.Last().Id;
            ApplyCanvasUnits(selected, PointerSelection.Objects, reference);
        }
        // Returns true while consuming a pending click or marquee; false lets existing movement run.
        private bool MoveSelectionPointer(MouseEventArgs e)
        {
            if (SelectingRectangle) { UpdateSelectionRectangle(e.Location); return true; }
            if (!PointerPending) return false;
            if (Math.Abs(e.X-PointerStart.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y-PointerStart.Y) < SystemInformation.DragSize.Height / 2) return true;
            PointerPending = false;
            if (!PointerGeometry)
            { SelectingRectangle = true; UpdateSelectionRectangle(e.Location); return true; }
            ApplyPointerClick(true); DragStartPoint = GrblPanel.ClientToMachine(PointerStart);
            if (ObjectModeButton.Checked)
            {
                MovingObjects = true; ObjectDragPaths = SelectedObjects.ToDictionary(o => o, o => o.Path.Clone());
                DragTransforms = GroupRasterObjects().ToDictionary(l => l.Id, l => new LayerTransformState(l));
            }
            else { MovingLayers = true; CaptureTransforms(); }
            return false;
        }
        private void UpdateSelectionRectangle(Point point)
        {
            var rectangle = CanvasLayout.SelectionRectangle(PointerStart, point);
            bool crossing = CanvasLayout.Crossing(PointerStart, point);
            var units = PointerUnits;
            var matched = units.Where(u => CanvasLayout.Matches(u, rectangle, crossing, GrblPanel.MachineToClient)).ToList();
            var selected = units.Where(u => matched.Contains(u) || (PointerAdd && u.IsSelected(PointerSelection))).ToList();
            Guid reference = matched.Count > 0 ? matched.Last().Id : PointerSelection.ReferenceId;
            ApplyCanvasUnits(selected, PointerSelection.Objects, reference);
            GrblPanel.SetSelectionRectangle(rectangle, crossing);
        }
        private bool EndSelectionPointer(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || (!PointerPending && !SelectingRectangle)) return false;
            bool marquee = SelectingRectangle;
            PointerPending = SelectingRectangle = false;
            if (marquee) UpdateSelectionRectangle(e.Location); else ApplyPointerClick(false);
            GrblPanel.SetSelectionRectangle(null, false); (GrblPanel as Control).Capture = false; return true;
        }
        private bool CancelSelectionPointer()
        {
            if (!PointerPending && !SelectingRectangle) return false;
            PointerPending = SelectingRectangle = false;
            GrblPanel.SetSelectionRectangle(null, false); (GrblPanel as Control).Capture = false;
            if (mCore != null && mCore.LayerController.IsLayered) RestorePointerSelection();
            return true;
        }
        private void ResetCanvasViewport()
        {
            DiscardInlineInput(); AffineGesture=null; CanvasPivot=null;
            PointerPending = SelectingRectangle = MovingObjects = MovingLayers = false;
            ResizingLayer = DrawingShape = false; ActiveCanvasTool = CanvasShape.Select;
            (GrblPanel as Control).Capture = false; if (ResizeHandle != null) ResizeHandle.Capture = false;
            SelectedObjects.Clear(); SelectedLayers.Clear(); SelectedRasterItems.Clear(); ReferenceId = Guid.Empty;
            GrblPanel.SetSelectionRectangle(null, false); GrblPanel.ResetViewport();
        }
        private void OnCanvasCaptureLost()
        {
            if (CancelSelectionPointer()) return;
            if (DrawingShape) { CancelCanvasTool(); return; }
            if (!MovingObjects && !MovingLayers) return;
            MovingObjects = MovingLayers = false;
            if (mCore != null) mCore.LayerController.CancelEdit();
        }
        private void OnCanvasViewportChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { if (IsHandleCreated) BeginInvoke((MethodInvoker)PositionResizeHandle); }
            else PositionResizeHandle();
        }
    }
}
