using System;
using System.Collections.Generic;
using System.Linq;

namespace LaserGRBL.Layering
{
    // Detached snapshots: neither later edits nor deleting the source affect paste.
    public sealed class CanvasClipboard
    {
        private List<LaserLayer> snapshot = new List<LaserLayer>();
        private int pasteCount;
        private bool cut;
        private List<CanvasGroup> groups = new List<CanvasGroup>();
        public List<Guid> LastPastedPaths { get; private set; } = new List<Guid>();
        public bool HasContent { get { return snapshot.Count > 0; } }
        public bool ContainsObjects { get; private set; }

        public void Clear() { snapshot.Clear(); groups.Clear(); pasteCount = 0; cut = false; ContainsObjects = false; LastPastedPaths.Clear(); }
        public void MarkCut() { cut = true; }

        public void CopyLayers(LaserDocument document, IEnumerable<LaserLayer> layers)
        {
            Clear();
            if (document == null) return;
            snapshot = layers.Where(document.Layers.Contains).Distinct().OrderBy(l => document.Layers.IndexOf(l)).Select(l => l.Clone(true)).ToList();
            CaptureGroups(document);
        }

        public void CopyObjects(LaserDocument document, IEnumerable<VectorSelection> objects, IEnumerable<LaserLayer> rasters = null)
        {
            Clear(); ContainsObjects = true;
            if (document == null) return;
            var selection = CanvasGroups.Expand(document, objects);
            foreach (var group in selection.GroupBy(o => o.Layer).OrderBy(g => g.Key.Order))
            {
                LaserLayer copy = group.Key.Clone(false);
                foreach (var item in group.GroupBy(o => o.Item))
                    copy.VectorItems.Add(new VectorItem { Text = item.Key.Text == null ? null : item.Key.Text.Clone(), SourceName = item.Key.SourceName, Paths = item.Select(o => o.Path).Distinct().Select(p => p.Clone(false)).ToList() });
                snapshot.Add(copy);
            }
            var roots = new HashSet<Guid>(selection.Select(o => CanvasGroups.Root(document, o.Path.GroupId)).Where(id => id != Guid.Empty));
            var rasterIds = new HashSet<Guid>((rasters ?? Enumerable.Empty<LaserLayer>()).Select(l => l.Id));
            snapshot.AddRange(document.Layers.Where(l => l.ContentKind == LayerContentKind.Raster && (rasterIds.Contains(l.Id) || roots.Contains(CanvasGroups.Root(document, l.GroupId)))).Select(l => l.Clone(true)));
            CaptureGroups(document);
        }

        private void CaptureGroups(LaserDocument document)
        {
            var used = new HashSet<Guid>(snapshot.Select(l => l.GroupId).Concat(snapshot.SelectMany(l => l.VectorItems).SelectMany(i => i.Paths).Select(p => p.GroupId)));
            foreach (var id in used.ToList())
            {
                Guid next = id; var visited = new HashSet<Guid>();
                while (next != Guid.Empty && visited.Add(next))
                {
                    used.Add(next); var parent = document.Groups.FirstOrDefault(g => g.Id == next); if (parent == null) break; next = parent.ParentId;
                }
            }
            groups = document.Groups.Where(g => used.Contains(g.Id)).Select(g => g.Clone()).ToList();
        }

        public IList<LaserLayer> Paste(LaserDocument document, bool editingLocked)
        {
            if (document == null || editingLocked || !HasContent) return new List<LaserLayer>();
            double offset = 5 * (cut ? pasteCount++ : ++pasteCount);
            var copies = snapshot.Select(layer => layer.Clone(true)).ToList();
            var groupIds = groups.ToDictionary(g => g.Id, g => Guid.NewGuid());
            Func<Guid, Guid> remap = id => groupIds.ContainsKey(id) ? groupIds[id] : Guid.Empty;
            document.Groups.AddRange(groups.Select(g => new CanvasGroup { Id = remap(g.Id), ParentId = remap(g.ParentId), Name = g.Name }));
            LastPastedPaths = copies.SelectMany(l => l.VectorItems).SelectMany(i => i.Paths).Select(p => p.Id).ToList();
            foreach (LaserLayer copy in copies)
            {
                copy.Name += LayerText.Get("CopySuffix");
                copy.GroupId = remap(copy.GroupId);
                foreach (var path in copy.VectorItems.SelectMany(i => i.Paths)) path.GroupId = remap(path.GroupId);
                if (copy.ContentKind == LayerContentKind.Raster) copy.OutputEnabled = false;
                copy.TranslateX += offset; copy.TranslateY += offset;
                document.Layers.Add(copy);
            }
            CanvasGroups.MergeColors(document);
            document.NormalizeOrder(); document.IsDirty = true;
            return copies.Select(c => document.Layers.FirstOrDefault(l => l.Id == c.Id) ?? document.FindByColor(c.Color, c.ContentKind)).Where(l => l != null).Distinct().ToList();
        }
    }
}
