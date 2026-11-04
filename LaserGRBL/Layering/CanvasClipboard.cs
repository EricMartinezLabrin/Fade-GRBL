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
        public bool HasContent { get { return snapshot.Count > 0; } }
        public bool ContainsObjects { get; private set; }

        public void Clear() { snapshot.Clear(); pasteCount = 0; ContainsObjects = false; }

        public void CopyLayers(LaserDocument document, IEnumerable<LaserLayer> layers)
        {
            Clear();
            if (document == null) return;
            snapshot = layers.Where(document.Layers.Contains).Distinct().OrderBy(l => l.Order).Select(l => l.Clone(true)).ToList();
        }

        public void CopyObjects(LaserDocument document, IEnumerable<VectorSelection> objects)
        {
            Clear(); ContainsObjects = true;
            var selection = objects.Where(o => o.IsCurrent(document)).ToList();
            foreach (var group in selection.GroupBy(o => o.Layer).OrderBy(g => g.Key.Order))
            {
                LaserLayer copy = group.Key.Clone(false);
                foreach (var item in group.GroupBy(o => o.Item))
                    copy.VectorItems.Add(new VectorItem { SourceName = item.Key.SourceName, Paths = item.Select(o => o.Path).Distinct().Select(p => p.Clone()).ToList() });
                snapshot.Add(copy);
            }
        }

        public IList<LaserLayer> Paste(LaserDocument document, bool editingLocked)
        {
            if (document == null || editingLocked || !HasContent) return new List<LaserLayer>();
            double offset = 5 * ++pasteCount;
            var copies = snapshot.Select(layer => layer.Clone(true)).ToList();
            foreach (LaserLayer copy in copies)
            {
                copy.Name += LayerText.Get("CopySuffix");
                copy.OutputEnabled = false;
                copy.TranslateX += offset; copy.TranslateY += offset;
                document.Layers.Add(copy);
            }
            document.NormalizeOrder(); document.IsDirty = true;
            return copies;
        }
    }
}
