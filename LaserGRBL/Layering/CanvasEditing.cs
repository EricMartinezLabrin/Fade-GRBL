using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace LaserGRBL.Layering
{
    public static class CanvasKeyboard
    {
        public static bool IsTextEditing(System.Windows.Forms.Control root)
        {
            if (root is System.Windows.Forms.TextBoxBase && root.Focused) return true;
            var grid = root as System.Windows.Forms.DataGridView;
            if (grid != null && grid.ContainsFocus) return true;
            var numeric = root as System.Windows.Forms.UpDownBase;
            if (numeric != null && numeric.ContainsFocus) return true;
            var combo = root as System.Windows.Forms.ComboBox;
            if (combo != null && combo.ContainsFocus) return true;
            foreach (System.Windows.Forms.Control child in root.Controls)
                if (child.ContainsFocus && IsTextEditing(child)) return true;
            return false;
        }
    }
    [Serializable]
    public class CanvasGroup
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ParentId { get; set; }
        public string Name { get; set; }
        public CanvasGroup Clone() { return (CanvasGroup)MemberwiseClone(); }
    }

    [Serializable]
    public class TextObject
    {
        public string Content { get; set; } = "";
        public string FontFamily { get; set; } = "Segoe UI";
        public FontStyle Style { get; set; }
        public double SizeMm { get; set; } = 10;
        public StringAlignment Alignment { get; set; }
        // Generated bounds, before subsequent geometry edits, allow font/content edits
        // to retain the object's current scale without stretching new words to old width.
        public bool HasAffinePose { get; set; }
        public double ReferenceOffsetX { get; set; }
        public double ReferenceOffsetY { get; set; }
        public double ReferenceWidth { get; set; }
        public double ReferenceHeight { get; set; }
        public TextObject Clone() { return (TextObject)MemberwiseClone(); }
    }

    public enum CanvasShape { Select, Rectangle, Ellipse, Line, Text }

    public static class CanvasObjects
    {
        public static RectangleF Bounds(IEnumerable<VectorPath> paths)
        {
            var points = paths.SelectMany(p => p.Points).ToList();
            return points.Count == 0 ? RectangleF.Empty : RectangleF.FromLTRB((float)points.Min(p => p.X), (float)points.Min(p => p.Y), (float)points.Max(p => p.X), (float)points.Max(p => p.Y));
        }

        public static List<VectorPath> FromGraphicsPath(GraphicsPath path)
        {
            path.Flatten(null, 0.025f);
            var result = new List<VectorPath>(); VectorPath current = null;
            var points = path.PathPoints; var types = path.PathTypes;
            for (int i = 0; i < points.Length; i++)
            {
                if ((types[i] & 7) == 0 || current == null) { current = new VectorPath(); result.Add(current); }
                current.Points.Add(new LaserPoint(points[i].X, -points[i].Y));
                if ((types[i] & 128) != 0) current.Closed = true;
            }
            return result.Where(p => p.Points.Count > 1).ToList();
        }

        public static VectorItem Shape(CanvasShape shape, PointF start, PointF end)
        {
            double left = Math.Min(start.X, end.X), bottom = Math.Min(start.Y, end.Y);
            double width = Math.Abs(end.X - start.X), height = Math.Abs(end.Y - start.Y);
            var path = new VectorPath { Closed = shape != CanvasShape.Line };
            if (shape == CanvasShape.Line) path.Points.AddRange(new[] { new LaserPoint(start.X, start.Y), new LaserPoint(end.X, end.Y) });
            else if (shape == CanvasShape.Rectangle)
                path.Points.AddRange(new[] { new LaserPoint(left, bottom), new LaserPoint(left + width, bottom), new LaserPoint(left + width, bottom + height), new LaserPoint(left, bottom + height) });
            else if (shape == CanvasShape.Ellipse)
            {
                // Bound chord error below 0.025 mm, matching text flattening tolerance.
                double radius = Math.Max(width, height) / 2;
                int steps = Math.Max(24, (int)Math.Ceiling(Math.PI / Math.Acos(Math.Max(-1, 1 - 0.025 / Math.Max(radius, 0.025)))));
                steps = Math.Min(20000, ((steps + 3) / 4) * 4);
                for (int i = 0; i < steps; i++) { double a = i * 2 * Math.PI / steps; path.Points.Add(new LaserPoint(left + width / 2 * (1 + Math.Cos(a)), bottom + height / 2 * (1 + Math.Sin(a)))); }
            }
            return new VectorItem { SourceName = shape.ToString(), Paths = new List<VectorPath> { path } };
        }

        private static readonly HashSet<string> AvailableFonts = LoadFonts();
        private static HashSet<string> LoadFonts()
        {
            var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var family in FontFamily.Families){result.Add(family.Name);family.Dispose();}
            return result;
        }
        public static bool FontAvailable(string name) { return AvailableFonts.Contains(name ?? ""); }

        public static VectorItem Text(TextObject text, PointF position, VectorItem previous = null)
        {
            if (string.IsNullOrWhiteSpace(text.Content) || !FontAvailable(text.FontFamily) || text.SizeMm <= 0 || text.SizeMm > 1000)
                throw new ArgumentException(LayerText.Get("InvalidText"));
            List<VectorPath> paths;
            using (var family = new FontFamily(text.FontFamily))
            using (var path = new GraphicsPath())
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                if (!family.IsStyleAvailable(text.Style)) throw new ArgumentException(LayerText.Get("InvalidFontStyle"));
                format.Alignment = text.Alignment;
                path.AddString(text.Content, family, (int)text.Style, (float)text.SizeMm, PointF.Empty, format);
                if (path.PointCount == 0) throw new ArgumentException(LayerText.Get("InvalidText"));
                paths = FromGraphicsPath(path);
            }
            var bounds = Bounds(paths); var old = previous == null ? RectangleF.Empty : Bounds(previous.Paths);
            double offsetX=0,offsetY=0;
            AffineTransform pose = AffineTransform.Translation(position.X,position.Y);
            if(previous != null && previous.Text != null)
            {
                if(FontAvailable(previous.Text.FontFamily))
                {
                    var reference=Text(previous.Text,PointF.Empty);
                    offsetX=bounds.Left-reference.Text.ReferenceOffsetX;offsetY=bounds.Top-reference.Text.ReferenceOffsetY;
                    pose=AffineTransform.Fit(reference.Paths.SelectMany(p=>p.Points).ToList(),previous.Paths.SelectMany(p=>p.Points).ToList()) ?? pose;
                }
                else
                {
                    offsetX=bounds.Left-previous.Text.ReferenceOffsetX;offsetY=bounds.Top-previous.Text.ReferenceOffsetY;
                    pose=previous.Text.HasAffinePose && previous.Paths.Count>0 ? previous.Paths[0].Pose.Clone() :
                        new AffineTransform { A=previous.Text.ReferenceWidth>0?old.Width/previous.Text.ReferenceWidth:1,
                            D=previous.Text.ReferenceHeight>0?old.Height/previous.Text.ReferenceHeight:1,X=old.Left,Y=old.Top };
                }
            }
            pose=pose.Then(AffineTransform.Translation(offsetX,offsetY));
            foreach (var point in paths.SelectMany(p=>p.Points)) { var q=pose.Apply(point.X-bounds.Left,point.Y-bounds.Top); point.X=q.X;point.Y=q.Y; }
            var metadata = text.Clone(); metadata.HasAffinePose=true; metadata.ReferenceOffsetX = bounds.Left; metadata.ReferenceOffsetY = bounds.Top; metadata.ReferenceWidth = bounds.Width; metadata.ReferenceHeight = bounds.Height;
            Guid group = previous == null || previous.Paths.Count == 0 ? Guid.Empty : previous.Paths[0].GroupId;
            foreach (var p in paths) { p.GroupId = group; p.Pose = pose.Clone(); }
            return new VectorItem { Id = previous == null ? Guid.NewGuid() : previous.Id, Text = metadata, SourceName = "Text", Paths = paths };
        }
    }

    public static class CanvasGroups
    {
        public static Guid Root(LaserDocument document, Guid id)
        {
            var visited = new HashSet<Guid>();
            while (id != Guid.Empty && visited.Add(id))
            {
                var group = document.Groups.FirstOrDefault(g => g.Id == id);
                if (group == null) return Guid.Empty;
                if (group.ParentId == Guid.Empty) return id;
                id = group.ParentId;
            }
            return Guid.Empty;
        }
        public static bool InGroup(LaserDocument document, Guid id, Guid ancestor)
        {
            var visited = new HashSet<Guid>();
            while (id != Guid.Empty && visited.Add(id))
            {
                if (id == ancestor) return true;
                var group = document.Groups.FirstOrDefault(g => g.Id == id); if (group == null) break;
                id = group.ParentId;
            }
            return false;
        }
        public static List<VectorSelection> Expand(LaserDocument document, IEnumerable<VectorSelection> selection)
        {
            if (document == null) return new List<VectorSelection>();
            selection = selection ?? Enumerable.Empty<VectorSelection>();
            var chosen = selection.Where(o => o.IsCurrent(document)).ToList();
            var roots = new HashSet<Guid>(chosen.Select(o => Root(document, o.Path.GroupId)).Where(id => id != Guid.Empty));
            var texts = new HashSet<Guid>(chosen.Where(o => o.Item.Text != null).Select(o => o.Item.Id));
            var ids = new HashSet<Guid>(chosen.Select(o => o.Path.Id));
            foreach (var layer in document.Layers)
                foreach (var item in layer.VectorItems)
                    foreach (var path in item.Paths)
                        if (ids.Contains(path.Id) || texts.Contains(item.Id) || roots.Contains(Root(document, path.GroupId)))
                            if (ids.Add(path.Id)) chosen.Add(new VectorSelection(layer, item, path));
            return chosen;
        }
        public static void MergeColors(LaserDocument document)
        {
            foreach (var sameColor in document.Layers.Where(l => l.ContentKind == LayerContentKind.Vector).GroupBy(l => l.ColorArgb).ToList())
            {
                var target = sameColor.First();
                foreach (var source in sameColor.Skip(1).ToList())
                {
                    if (target.GroupId != source.GroupId)
                    {
                        foreach (var path in target.VectorItems.SelectMany(i => i.Paths))
                            if (path.GroupId == Guid.Empty) path.GroupId = target.GroupId;
                        target.GroupId = Guid.Empty;
                    }
                    foreach (var item in source.VectorItems)
                    {
                        foreach (var path in item.Paths)
                        {
                            if (path.GroupId == Guid.Empty) path.GroupId = source.GroupId;
                            AffineTransform.Path(path, AffineTransform.Layer(target).Inverse().Then(AffineTransform.Layer(source)));
                        }
                        target.VectorItems.Add(item);
                    }
                    document.Layers.Remove(source);
                }
            }
            document.NormalizeOrder();
        }
    }

    public class CanvasSelectionState
    {
        public Guid ReferenceId { get; set; }
        public List<Guid> Layers { get; set; } = new List<Guid>();
        public List<Guid> Paths { get; set; } = new List<Guid>();
        public bool Objects { get; set; }
        public CanvasSelectionState Clone() { return new CanvasSelectionState { ReferenceId = ReferenceId, Layers = new List<Guid>(Layers), Paths = new List<Guid>(Paths), Objects = Objects }; }
    }

    // Changed() is the transaction boundary; live drag previews never commit.
    public sealed class CanvasHistory
    {
        private sealed class State { public LaserDocument Document; public string Key; public CanvasSelectionState Selection; }
        private readonly List<State> undo = new List<State>();
        private readonly List<State> redo = new List<State>();
        private State current;
        private string savedKey;
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(LaserDocument));
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public static string Key(LaserDocument document) { using (var writer = new StringWriter()) { Serializer.Serialize(writer, document); return writer.ToString(); } }
        public void Reset(LaserDocument document, bool saved = true) { undo.Clear(); redo.Clear(); current = Capture(document, new CanvasSelectionState()); savedKey = saved ? current.Key : null; document.IsDirty = !saved; }
        private static State Capture(LaserDocument doc, CanvasSelectionState selection) { return new State { Document = doc.Clone(), Key = Key(doc), Selection = selection.Clone() }; }
        public void Select(CanvasSelectionState selection) { if (current != null) current.Selection = selection.Clone(); }
        public void Commit(LaserDocument document, CanvasSelectionState selection)
        {
            if (current == null) { Reset(document, false); return; }
            string key = Key(document);
            if (key != current.Key)
            {
                undo.Add(current); if (undo.Count > 100) undo.RemoveAt(0);
                redo.Clear(); current = Capture(document, selection);
            }
            document.IsDirty = current.Key != savedKey;
        }
        public void MarkSaved() { if (current != null) savedKey = current.Key; }
        public LaserDocument Restore(bool forward, out CanvasSelectionState selection)
        {
            var from = forward ? redo : undo; var to = forward ? undo : redo;
            selection = null; if (from.Count == 0) return null;
            to.Add(current); current = from[from.Count - 1]; from.RemoveAt(from.Count - 1);
            selection = current.Selection.Clone(); var document = current.Document.Clone(); document.IsDirty = current.Key != savedKey; return document;
        }
        public LaserDocument Cancel() { if (current == null) return null; var doc = current.Document.Clone(); doc.IsDirty = current.Key != savedKey; return doc; }
    }
}
