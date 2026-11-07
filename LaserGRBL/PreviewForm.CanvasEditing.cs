using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LaserGRBL.Layering;
using LaserGRBL.UserControls;
using LaserGRBL.Icons;

namespace LaserGRBL
{
    public partial class PreviewForm
    {
        private ToolStripButton UndoButton, RedoButton, CutButton;
        private ToolStrip EditingToolbar;
        private ToolStripDropDownButton AddObjectButton, GroupObjectsButton, TextOptionsButton;
        private CanvasShape ActiveCanvasTool;
        private bool DrawingShape, RestoringSelection;
        private bool WidthInvalid, HeightInvalid;
        private bool WidthEdited, HeightEdited, UpdatingDimensionText;
        private PointF ShapeStart;
        private TextObject PendingText;
        private void BuildCanvasEditingTools()
        {
            EditingToolbar = new ToolStrip { Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden, MinimumSize = new Size(0, 32) };
            UndoButton = CanvasButton("Undo", "Ctrl+Z", () => { if (InlineText != null) { InlineText.Undo(); InlineText.Focus(); } else mCore?.LayerController.Undo(); });
            RedoButton = CanvasButton("Redo", "Ctrl+Shift+Z", () => { if (InlineText != null) { InlineText.Redo(); InlineText.Focus(); } else mCore?.LayerController.Redo(); });
            CutButton = CanvasButton("Cut", "Ctrl+X", CutCanvasSelection);
            IconsMgr.PrepareButton(UndoButton, "mdi-undo", new Size(20, 20));
            IconsMgr.PrepareButton(RedoButton, "mdi-redo", new Size(20, 20));
            IconsMgr.PrepareButton(CutButton, "mdi-content-cut", new Size(20, 20));
            foreach (var button in new[] { UndoButton, RedoButton, CutButton })
            { button.DisplayStyle = ToolStripItemDisplayStyle.Image; button.AutoSize = false; button.Size = new Size(32, 32); button.AccessibleName = button.Text; }
            GroupObjectsButton = new ToolStripDropDownButton(LayerText.Get("Group"));
            GroupObjectsButton.DropDownItems.Add(LayerText.Get("Group") + " (Ctrl+G)", null, (s, e) => GroupCanvas(false));
            GroupObjectsButton.DropDownItems.Add(LayerText.Get("Ungroup") + " (Ctrl+Shift+G)", null, (s, e) => GroupCanvas(true));
            AddObjectButton = new ToolStripDropDownButton(LayerText.Get("AddObject"));
            foreach (var shape in new[] { CanvasShape.Rectangle, CanvasShape.Ellipse, CanvasShape.Line, CanvasShape.Text })
            {
                var tool = shape;
                AddObjectButton.DropDownItems.Add(LayerText.Get(shape == CanvasShape.Text ? "TextTool" : "Shape" + shape), null, (s, e) => SelectCanvasTool(tool));
            }
            TextOptionsButton = new ToolStripDropDownButton(LayerText.Get("TextTool"));
            TextOptionsButton.DropDownItems.Add(LayerText.Get("EditText"), null, (s, e) => EditCanvasText());
            TextOptionsButton.DropDownItems.Add(LayerText.Get("ConvertText"), null, (s, e) => { if (mCore != null) mCore.LayerController.ConvertText(SelectedObjects); });
            EditingToolbar.Items.AddRange(new ToolStripItem[] { AddObjectButton, UndoButton, RedoButton, CutButton, GroupObjectsButton, TextOptionsButton });
            BuildAlignmentTools();
            BuildDirectEditingTools();
            FadeTheme.Apply(EditingToolbar);
        }
        private static ToolStripButton CanvasButton(string key, string shortcut, Action action)
        {
            var button = new ToolStripButton(LayerText.Get(key)) { ToolTipText = LayerText.Get(key) + " (" + shortcut + ")" };
            button.Click += (s, e) => action(); return button;
        }
        private void InstallCanvasMenu(Control canvas)
        {
            var menu = canvas.ContextMenuStrip ?? new ContextMenuStrip();
            if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
            AddCanvasMenuItem(menu, "Undo", Keys.Control | Keys.Z, () => { if (InlineText != null) { InlineText.Undo(); InlineText.Focus(); } else mCore?.LayerController.Undo(); });
            AddCanvasMenuItem(menu, "Redo", Keys.Control | Keys.Shift | Keys.Z, () => { if (InlineText != null) { InlineText.Redo(); InlineText.Focus(); } else mCore?.LayerController.Redo(); });
            AddCanvasMenuItem(menu, "Cut", Keys.Control | Keys.X, CutCanvasSelection);
            AddCanvasMenuItem(menu, "Copy", Keys.Control | Keys.C, CopyCanvasSelection);
            AddCanvasMenuItem(menu, "Paste", Keys.Control | Keys.V, () => PasteCanvasSelection(false));
            AddCanvasMenuItem(menu, "Duplicate", Keys.Control | Keys.D, () => PasteCanvasSelection(true));
            AddCanvasMenuItem(menu, "Group", Keys.Control | Keys.G, () => GroupCanvas(false));
            AddCanvasMenuItem(menu, "Ungroup", Keys.Control | Keys.Shift | Keys.G, () => GroupCanvas(true));
            AddCanvasMenuItem(menu, "EditText", Keys.None, EditCanvasText);
            AddCanvasMenuItem(menu, "ConvertText", Keys.None, () => mCore?.LayerController.ConvertText(SelectedObjects));
            menu.Opening += (s, e) =>
            {
                if (InlineText != null) { e.Cancel = true; return; }
                bool editable = mCore != null && !mCore.JobEditingLocked && mCore.LayerController.IsLayered;
                foreach (ToolStripItem item in menu.Items)
                {
                    string key = item.Tag as string; if (key == null) continue;
                    item.Enabled = editable;
                    if (key == "Undo") item.Enabled = editable && mCore.LayerController.CanUndo;
                    if (key == "Redo") item.Enabled = editable && mCore.LayerController.CanRedo;
                    if (key == "Paste") item.Enabled = editable && mCore.LayerController.Clipboard.HasContent;
                    if (key == "EditText" || key == "ConvertText") item.Enabled = editable && SelectedObjects.Any(o => o.Item.Text != null);
                }
            };
            FadeTheme.Apply(menu); canvas.ContextMenuStrip = menu;
        }
        private void AddCanvasMenuItem(ContextMenuStrip menu, string key, Keys shortcut, Action action)
        {
            var item = new ToolStripMenuItem(LayerText.Get(key)) { ShortcutKeys = shortcut, Tag = key };
            item.Click += (s, e) => action(); menu.Items.Add(item);
        }
        private void RefreshCanvasTools(bool layered, bool hasSelection)
        {
            if (UndoButton == null) return;
            EditingToolbar.Enabled = mCore != null && !mCore.JobEditingLocked;
            UndoButton.Enabled = layered && mCore.LayerController.CanUndo;
            RedoButton.Enabled = layered && mCore.LayerController.CanRedo;
            CutButton.Enabled = layered && hasSelection;
            GroupObjectsButton.Enabled = layered && hasSelection;
            if (layered)
            {
                Guid id = ObjectModeButton.Checked ? SelectedObjects.Select(o => o.Path.GroupId).FirstOrDefault() : SelectedLayers.Select(l => l.GroupId).FirstOrDefault();
                var group = mCore.LayerController.Document.Groups.FirstOrDefault(g => g.Id == CanvasGroups.Root(mCore.LayerController.Document, id));
                GroupObjectsButton.Text = group == null ? LayerText.Get("Group") : string.Format(LayerText.Get("GroupMembership"), group.Name);
            }
            TextOptionsButton.Enabled = layered && SelectedObjects.Any(o => o.Item.Text != null);
            AddObjectButton.Enabled = mCore != null && !mCore.JobEditingLocked && (mCore.LayerController.IsLayered || !mCore.HasProgram);
            if (InlineText != null)
            {
                UndoButton.Enabled = InlineText.CanUndo; RedoButton.Enabled = InlineText.CanRedo;
                CutButton.Enabled = GroupObjectsButton.Enabled = TextOptionsButton.Enabled = AddObjectButton.Enabled = false;
            }
            RefreshAlignmentTools(layered && InlineText == null);
            RefreshTransformTools(layered && hasSelection);
        }
        private void RefreshHistoryTools()
        {
            bool layered = mCore != null && mCore.LayerController.IsLayered;
            UndoButton.Enabled = layered && mCore.LayerController.CanUndo;
            RedoButton.Enabled = layered && mCore.LayerController.CanRedo;
        }
        private bool HandleCanvasCommand(Keys keys)
        {
            if ((keys == (Keys.Control | Keys.Z) || keys == (Keys.Control | Keys.Shift | Keys.Z)) && CancelActiveGesture()) return true;
            if (keys == (Keys.Control | Keys.Z)) { mCore.LayerController.Undo(); return true; }
            if (keys == (Keys.Control | Keys.Shift | Keys.Z)) { mCore.LayerController.Redo(); return true; }
            if (keys == (Keys.Control | Keys.X)) { CutCanvasSelection(); return true; }
            if (keys == (Keys.Control | Keys.G)) { GroupCanvas(false); return true; }
            if (keys == (Keys.Control | Keys.Shift | Keys.G)) { GroupCanvas(true); return true; }
            return false;
        }
        private bool CancelActiveGesture()
        {
            if (CancelAffineGesture()) return true;
            if (CancelSelectionPointer()) return true;
            if (InlineText != null) { FinishInlineText(false); return true; }
            if (DrawingShape || ActiveCanvasTool != CanvasShape.Select) { CancelCanvasTool(); return true; }
            if (!MovingObjects && !MovingLayers && !ResizingLayer) return false;
            MovingObjects = MovingLayers = ResizingLayer = false;
            (GrblPanel as Control).Capture = false; ResizeHandle.Capture = false;
            mCore.LayerController.CancelEdit(); return true;
        }
        private void CutCanvasSelection()
        {
            if (mCore == null || mCore.JobEditingLocked || !mCore.LayerController.IsLayered) return;
            if (ObjectModeButton.Checked && !LayerManager.ContainsFocus) mCore.LayerController.CutObjects(SelectedObjects, SelectedRasterItems);
            else mCore.LayerController.CutLayers(SelectedLayers);
            SelectedObjects.Clear(); SelectedRasterItems.Clear(); RefreshSelectionEditor();
        }
        private void GroupCanvas(bool ungroup)
        {
            if (mCore == null || mCore.JobEditingLocked || !mCore.LayerController.IsLayered) return;
            if (ungroup) mCore.LayerController.Ungroup(SelectedLayers, SelectedObjects, ObjectModeButton.Checked);
            else mCore.LayerController.Group(SelectedLayers, SelectedObjects, ObjectModeButton.Checked);
            RefreshSelectionEditor();
        }
        private void ExpandObjectSelection()
        {
            var expanded = CanvasGroups.Expand(mCore.LayerController.Document, SelectedObjects);
            SelectedObjects.Clear(); SelectedObjects.AddRange(expanded);
        }
        private void RememberCanvasSelection()
        {
            if (RestoringSelection || mCore == null || !mCore.LayerController.IsLayered) return;
            var document = mCore.LayerController.Document;
            SelectedLayers.RemoveAll(l => !document.Layers.Contains(l));
            mCore.LayerController.SetSelection(CurrentCanvasSelection());
        }
        private void RestoreCanvasSelection(CanvasSelectionState state)
        {
            if (mCore == null || !mCore.LayerController.IsLayered) return;
            ClearSizeErrors();
            RestoringSelection = true;
            try
            {
                var document = mCore.LayerController.Document;
                ObjectModeButton.Checked = state.Objects;
                ReferenceId = state.ReferenceId;
                SelectedRasterItems.Clear();
                if (state.Objects) SelectedRasterItems.AddRange(document.Layers.Where(l => l.ContentKind == LayerContentKind.Raster && state.Layers.Contains(l.Id)));
                LayerManager.SelectLayers(document.Layers.Where(l => state.Layers.Contains(l.Id)).ToList());
                SelectedObjects.Clear();
                foreach (var layer in document.Layers)
                    foreach (var item in layer.VectorItems)
                        foreach (var path in item.Paths)
                            if (state.Paths.Contains(path.Id)) SelectedObjects.Add(new VectorSelection(layer, item, path));
                if (state.Objects) LayerManager.SelectLayers(SelectedObjects.Select(o => o.Layer).Concat(SelectedRasterItems).Distinct().ToList());
                RefreshSelectionEditor();
                RectangleF bounds;
                if (TrySelectionBounds(out bounds))
                {
                    SetDimensionText(WidthBox, bounds.Width.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture));
                    SetDimensionText(HeightBox, bounds.Height.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture));
                }
            }
            finally { RestoringSelection = false; }
        }
        private void SelectCanvasTool(CanvasShape tool)
        {
            if (mCore == null || mCore.JobEditingLocked || (!mCore.LayerController.IsLayered && mCore.HasProgram)) return;
            if (!mCore.LayerController.IsLayered) mCore.LayerController.NewDocument();
            ActiveCanvasTool = tool; RefreshSelectionEditor(); (GrblPanel as Control).Cursor = Cursors.Cross; (GrblPanel as Control).Focus();
        }
        private bool BeginCanvasTool(PointF machine, Control canvas)
        {
            if (ActiveCanvasTool == CanvasShape.Select) return false;
            if (ActiveCanvasTool == CanvasShape.Text)
            {
                ActiveCanvasTool = CanvasShape.Select; BeginInlineText(null,machine); return true;
            }
            ShapeStart = machine; DrawingShape = true; canvas.Capture = true; return true;
        }
        private void UpdateShapePreview(PointF end)
        {
            var shape = CanvasObjects.Shape(ActiveCanvasTool, ShapeStart, end);
            GrblPanel.SetSelectionContours(shape.Paths); GrblPanel.SetSelectionBounds(CanvasObjects.Bounds(shape.Paths));
        }
        private void FinishCanvasShape(PointF end, Control canvas)
        {
            var tool = ActiveCanvasTool; DrawingShape = false; canvas.Capture = false;
            if (tool == CanvasShape.Line ? Math.Abs(end.X - ShapeStart.X) + Math.Abs(end.Y - ShapeStart.Y) >= .01 : Math.Abs(end.X - ShapeStart.X) >= .01 && Math.Abs(end.Y - ShapeStart.Y) >= .01)
                InsertCanvasObject(CanvasObjects.Shape(tool, ShapeStart, end));
            CancelCanvasTool();
        }
        private void CancelCanvasTool()
        {
            DrawingShape = false; ActiveCanvasTool = CanvasShape.Select;
            (GrblPanel as Control).Capture = false; (GrblPanel as Control).Cursor = Cursors.Default; RefreshSelectionEditor();
        }
        private void InsertCanvasObject(VectorItem item)
        {
            var target = SelectedLayers.FirstOrDefault(l => l.ContentKind == LayerContentKind.Vector);
            var layer = mCore.LayerController.InsertObject(item, target); if (layer == null) return;
            ObjectModeButton.Checked = true; LayerManager.SelectLayers(new[] { layer }); SelectedObjects.Clear();
            foreach (var path in item.Paths) SelectedObjects.Add(new VectorSelection(layer, item, path));
            RefreshSelectionEditor();
        }
        private void EditCanvasText()
        {
            EditCanvasText(SelectedObjects.FirstOrDefault(o => o.Item.Text != null));
        }
        private void EditCanvasText(VectorSelection selected)
        {
            if (mCore == null || mCore.JobEditingLocked || !mCore.LayerController.IsLayered) return;
            if (selected == null || selected.Item.Text == null) return;
            BeginInlineText(selected, PointF.Empty);
        }
        private void ApplyObjectSize(bool widthChanged)
        {
            if (SelectedObjects.Count == 0 && SelectedRasterItems.Count == 0) return;
            var source = widthChanged ? WidthBox : HeightBox; double value;
            if (!TryReadDimension(source.Text, out value) || value <= 0) { ShowSizeError(source); return; }
            ClearSizeError(source);
            RectangleF bounds; if (!TrySelectionBounds(out bounds)) return;
            ConfirmDimensionEdit(source);
            if (DimensionUnchanged(value, widthChanged ? bounds.Width : bounds.Height)) return;
            if ((widthChanged && bounds.Width <= 0) || (!widthChanged && bounds.Height <= 0)) return;
            double sx = widthChanged ? value / bounds.Width : 1, sy = widthChanged ? 1 : value / bounds.Height;
            if (LockAspectButton.Checked) { if (widthChanged) sy = sx; else sx = sy; }
            ResizeObjects(bounds, sx, sy); CommitCanvasTransform();
        }
        private bool TrySelectionBounds(out RectangleF bounds)
        {
            if (ObjectModeButton.Checked)
            {
                bounds = SelectedObjects.Count == 0 ? RectangleF.Empty : SelectedObjects.Select(o => o.Bounds).Aggregate(RectangleF.Union);
                bool found = SelectedObjects.Count > 0;
                foreach (var raster in GroupRasterObjects())
                {
                    RectangleF rasterBounds;
                    if (LayerGeometry.TryGetBounds(raster, out rasterBounds)) { bounds = found ? RectangleF.Union(bounds, rasterBounds) : rasterBounds; found = true; }
                }
                return found;
            }
            return LayerGeometry.TryGetBounds(SelectedLayers, out bounds);
        }
        private void ResizeObjects(RectangleF bounds, double sx, double sy, bool preserveMaximumY = false)
        {
            double anchorY = preserveMaximumY ? bounds.Bottom : bounds.Top;
            var resize = AffineTransform.Around(new AffineTransform { A=sx,D=sy },new PointF(bounds.Left,(float)anchorY));
            foreach (var selection in SelectedObjects) AffineTransform.WorldPath(selection,resize);
            foreach (var raster in GroupRasterObjects())
            {
                raster.ScaleX *= sx; raster.CrossX *= sx; raster.ScaleY *= sy; raster.CrossY *= sy;
                raster.TranslateX = bounds.Left + (raster.TranslateX - bounds.Left) * sx;
                raster.TranslateY = anchorY + (raster.TranslateY - anchorY) * sy;
            }
        }
        private List<LaserLayer> GroupRasterObjects()
        {
            if (mCore == null || !mCore.LayerController.IsLayered) return new List<LaserLayer>();
            var doc = mCore.LayerController.Document;
            var roots = new HashSet<Guid>(SelectedObjects.Select(o => CanvasGroups.Root(doc, o.Path.GroupId)).Where(id => id != Guid.Empty));
            SelectedRasterItems.RemoveAll(l => !doc.Layers.Contains(l));
            return doc.Layers.Where(l => l.ContentKind == LayerContentKind.Raster && (SelectedRasterItems.Contains(l) || roots.Contains(CanvasGroups.Root(doc, l.GroupId)))).ToList();
        }
        private void ShowSizeError(ToolStripTextBox source)
        {
            if (source == WidthBox) WidthInvalid = true; else HeightInvalid = true;
            source.ToolTipText = LayerText.Get("InvalidValue"); source.ForeColor = ColorScheme.PreviewCommandKO;
        }
        private void ClearSizeError(ToolStripTextBox source)
        {
            if (source == WidthBox) WidthInvalid = false; else HeightInvalid = false;
            source.ToolTipText = LayerText.Get(source == WidthBox ? "WidthTooltip" : "HeightTooltip"); source.ForeColor = ColorScheme.FormForeColor;
        }
        private void ClearSizeErrors() { ClearSizeError(WidthBox); ClearSizeError(HeightBox); WidthEdited = HeightEdited = false; }
        private void ConfirmDimensionEdit(ToolStripTextBox source) { if (source == WidthBox) WidthEdited = false; else HeightEdited = false; }
        private void SetDimensionText(ToolStripTextBox source, string text)
        {
            UpdatingDimensionText = true;
            try { source.Text = text; source.TextBox.ClearUndo(); ConfirmDimensionEdit(source); }
            finally { UpdatingDimensionText = false; }
        }
        private void RefreshDimensionText(RectangleF bounds)
        {
            if (!WidthBox.Focused && !WidthInvalid) SetDimensionText(WidthBox, bounds.Width.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture));
            if (!HeightBox.Focused && !HeightInvalid) SetDimensionText(HeightBox, bounds.Height.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture));
        }
        private static bool DimensionUnchanged(double value, double current)
        {
            double displayed;
            return Math.Abs(value-current) < 1e-9 || (TryReadDimension(current.ToString("0.###",System.Globalization.CultureInfo.CurrentCulture),out displayed) && Math.Abs(value-displayed) < 1e-9);
        }
        private void MoveGroupRasters(double dx, double dy)
        {
            foreach (var raster in GroupRasterObjects())
            {
                LayerTransformState state;
                if (DragTransforms.TryGetValue(raster.Id, out state)) { raster.TranslateX = state.TranslateX + dx; raster.TranslateY = state.TranslateY + dy; }
            }
        }
    }
}
