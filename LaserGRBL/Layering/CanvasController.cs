using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace LaserGRBL.Layering
{
    public partial class LayerDocumentController
    {
        public CanvasHistory History { get; private set; } = new CanvasHistory();
        public CanvasSelectionState Selection { get; private set; } = new CanvasSelectionState();
        public event Action<CanvasSelectionState> SelectionRestored;
        public event Action HistoryStateChanged;
        public event Action DocumentReplaced;
        public bool Align(CanvasAlignmentAction action, bool toTable)
        {
            if (Document == null || core.JobEditingLocked) return false;
            RectangleF? table = null;
            if (toTable) table = new RectangleF(-(float)core.WorkingOffset.X, -(float)core.WorkingOffset.Y, (float)GrblCore.Configuration.TableWidth, (float)GrblCore.Configuration.TableHeight);
            bool changed = CanvasLayout.Align(Document, Selection, action, table);
            if (changed) Changed();
            return changed;
        }
        private bool restoringHistory;
        public bool CanUndo { get { return !core.JobEditingLocked && History.CanUndo; } }
        public bool CanRedo { get { return !core.JobEditingLocked && History.CanRedo; } }
        private void ResetHistory(bool saved = true) { transientEdit=false; Selection = new CanvasSelectionState(); History.Reset(Document, saved); }
        private void FinishImport(bool replace)
        {
            if (!replace) { Changed(); return; }
            CanvasGroups.MergeColors(Document); ResetHistory(false); NotifyDocumentChanged(); RequestCompile();
        }
        private bool transientEdit;
        public void BeginTransientEdit() { if (!core.JobEditingLocked) { History.Select(Selection); transientEdit=true; PreviewChanged(); } }
        public void SetSelection(CanvasSelectionState selection) { if (!restoringHistory) { Selection = selection.Clone(); if(!transientEdit) History.Select(Selection); } }
        public void RecordCellEdit()
        {
            if (core.JobEditingLocked || Document == null) return;
            History.Commit(Document, Selection);
            InvalidateCompilation();
            IsCompilationCurrent = false;
            HistoryStateChanged?.Invoke(); RaiseCompilationState();
        }
        public void Undo() { RestoreHistory(false); }
        public void Redo() { RestoreHistory(true); }
        private void RestoreHistory(bool forward)
        {
            if (core.JobEditingLocked || Document == null) return;
            History.Commit(Document, Selection);
            if (forward ? !CanRedo : !CanUndo) return;
            CanvasSelectionState selection;
            var restored = History.Restore(forward, out selection);
            if (restored == null) return;
            InvalidateCompilation();
            Document = restored; Selection = selection;
            restoringHistory = true;
            try { NotifyDocumentChanged(); SelectionRestored?.Invoke(selection); }
            finally { restoringHistory = false; }
            RequestCompile(true);
        }
        public void CancelEdit()
        {
            transientEdit=false;
            if (core.JobEditingLocked || Document == null) return;
            var restored = History.Cancel(); if (restored == null) return;
            Document = restored;
            restoringHistory = true;
            try { NotifyDocumentChanged(); SelectionRestored?.Invoke(Selection.Clone()); }
            finally { restoringHistory = false; }
            RequestCompile(true);
        }
        public void SetColor(LaserLayer layer, Color color)
        {
            if (core.JobEditingLocked || Document == null || !Document.Layers.Contains(layer)) return;
            var target = Document.Layers.FirstOrDefault(l => l != layer && l.ContentKind == LayerContentKind.Vector && l.ColorArgb == color.ToArgb());
            layer.Color = color;
            // Existing color owns the settings even when the recolored layer precedes it.
            if (target != null && layer.ContentKind == LayerContentKind.Vector)
            {
                int sourceIndex = Document.Layers.IndexOf(layer);
                int targetIndex = Document.Layers.IndexOf(target);
                if (sourceIndex < targetIndex) { Document.Layers.Remove(layer); Document.Layers.Insert(Document.Layers.IndexOf(target) + 1, layer); }
            }
            Changed();
        }
        public void CutLayers(IEnumerable<LaserLayer> layers)
        {
            if (core.JobEditingLocked || Document == null) return;
            var chosen = layers.Where(Document.Layers.Contains).ToList(); if (chosen.Count == 0) return;
            Clipboard.CopyLayers(Document, chosen); Clipboard.MarkCut(); RemoveLayers(chosen);
        }
        public void CutObjects(IEnumerable<VectorSelection> objects, IEnumerable<LaserLayer> rasters = null)
        {
            if (core.JobEditingLocked || Document == null) return;
            var chosen = CanvasGroups.Expand(Document, objects); var images = (rasters ?? Enumerable.Empty<LaserLayer>()).ToList(); if (chosen.Count == 0 && images.Count == 0) return;
            Clipboard.CopyObjects(Document, chosen, images); Clipboard.MarkCut();
            DeleteObjects(chosen, images);
        }
        public void DeleteObjects(IEnumerable<VectorSelection> objects, IEnumerable<LaserLayer> rasters = null)
        {
            if (core.JobEditingLocked || Document == null) return;
            var chosen = CanvasGroups.Expand(Document, objects);
            var roots = new HashSet<Guid>(chosen.Select(o => CanvasGroups.Root(Document, o.Path.GroupId)).Where(id => id != Guid.Empty));
            var images = new HashSet<LaserLayer>(rasters ?? Enumerable.Empty<LaserLayer>());
            Document.Layers.RemoveAll(l => l.ContentKind == LayerContentKind.Raster && (images.Contains(l) || roots.Contains(CanvasGroups.Root(Document, l.GroupId))));
            foreach (var item in chosen) item.Delete(); Changed();
        }
        public Guid Group(IEnumerable<LaserLayer> layers, IEnumerable<VectorSelection> objects, bool objectMode)
        {
            if (core.JobEditingLocked || Document == null) return Guid.Empty;
            var chosenLayers = layers.Where(Document.Layers.Contains).Distinct().ToList();
            if (objectMode) chosenLayers = chosenLayers.Where(l => l.ContentKind == LayerContentKind.Raster).ToList();
            var chosen = CanvasGroups.Expand(Document, objects);
            if ((!objectMode && chosenLayers.Count < 2) || (objectMode && chosen.Select(o => o.Item.Text == null ? o.Path.Id : o.Item.Id).Distinct().Count() + chosenLayers.Count < 2)) return Guid.Empty;
            var group = new CanvasGroup { Name = LayerText.Get("GroupName") + " " + (Document.Groups.Count + 1) };
            var roots = new HashSet<Guid>();
            Action<Guid> parent = id => { var root = CanvasGroups.Root(Document, id); if (root != Guid.Empty) roots.Add(root); };
            if (objectMode)
            {
                foreach (var item in chosen) { parent(item.Path.GroupId); if (item.Path.GroupId == Guid.Empty) item.Path.GroupId = group.Id; }
                foreach (var layer in chosenLayers) { parent(layer.GroupId); if (layer.GroupId == Guid.Empty) layer.GroupId = group.Id; }
            }
            else
                foreach (var layer in chosenLayers)
                {
                    parent(layer.GroupId); if (layer.GroupId == Guid.Empty) layer.GroupId = group.Id;
                    foreach (var path in layer.VectorItems.SelectMany(i => i.Paths)) { parent(path.GroupId); if (path.GroupId == Guid.Empty) path.GroupId = group.Id; }
                }
            foreach (var root in Document.Groups.Where(g => roots.Contains(g.Id))) root.ParentId = group.Id;
            Document.Groups.Add(group); Changed(); return group.Id;
        }
        public void Ungroup(IEnumerable<LaserLayer> layers, IEnumerable<VectorSelection> objects, bool objectMode)
        {
            if (core.JobEditingLocked || Document == null) return;
            var ids = objectMode ? objects.Where(o => o.IsCurrent(Document)).Select(o => o.Path.GroupId).Concat(layers.Where(l => Document.Layers.Contains(l) && l.ContentKind == LayerContentKind.Raster).Select(l => l.GroupId)) : layers.Where(Document.Layers.Contains).SelectMany(l => new[] { l.GroupId }.Concat(l.VectorItems.SelectMany(i => i.Paths).Select(p => p.GroupId)));
            var roots = new HashSet<Guid>(ids.Select(id => CanvasGroups.Root(Document, id)).Where(id => id != Guid.Empty));
            if (roots.Count == 0) return;
            foreach (var layer in Document.Layers)
            {
                if (roots.Contains(layer.GroupId)) layer.GroupId = Guid.Empty;
                foreach (var path in layer.VectorItems.SelectMany(i => i.Paths)) if (roots.Contains(path.GroupId)) path.GroupId = Guid.Empty;
            }
            foreach (var child in Document.Groups.Where(g => roots.Contains(g.ParentId))) child.ParentId = Guid.Empty;
            Document.Groups.RemoveAll(g => roots.Contains(g.Id)); Changed();
        }
        public LaserLayer InsertObject(VectorItem item, LaserLayer target, bool commit = true)
        {
            if (core.JobEditingLocked) return null;
            EnsureDocument();
            if (target == null || !Document.Layers.Contains(target) || target.ContentKind != LayerContentKind.Vector)
            {
                target = new LaserLayer { Name = LayerText.Get("NewLayer"), Color = NextColor() }; Document.Layers.Add(target);
            }
            // New object geometry is supplied in machine coordinates.
            foreach (var path in item.Paths) AffineTransform.Path(path, AffineTransform.Layer(target).Inverse());
            target.VectorItems.Add(item); if (commit) Changed(); else PreviewChanged(); return target;
        }
        public void UpdateText(LaserLayer layer, VectorItem item, TextObject text)
        {
            if (core.JobEditingLocked || Document == null || !Document.Layers.Contains(layer) || !layer.VectorItems.Contains(item)) return;
            var bounds = CanvasObjects.Bounds(item.Paths);
            var replacement = CanvasObjects.Text(text, new PointF(bounds.Left, bounds.Top), item);
            layer.VectorItems[layer.VectorItems.IndexOf(item)] = replacement;
            Changed();
        }
        public void ConvertText(IEnumerable<VectorSelection> selection)
        {
            if (core.JobEditingLocked || Document == null) return;
            foreach (var item in selection.Where(o => o.IsCurrent(Document)).Select(o => o.Item).Distinct()) item.Text = null;
            Changed();
        }
    }
}
