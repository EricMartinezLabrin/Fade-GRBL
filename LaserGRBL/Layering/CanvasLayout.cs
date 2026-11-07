using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace LaserGRBL.Layering
{
    public enum CanvasAlignmentAction { Left, HorizontalCenter, Right, Top, VerticalCenter, Bottom, Center }

    public sealed class CanvasUnit
    {
        public Guid Id;
        public string Name;
        public readonly List<LaserLayer> Layers = new List<LaserLayer>();
        public readonly List<VectorSelection> Objects = new List<VectorSelection>();
        public readonly List<VectorPath> VisiblePaths = new List<VectorPath>();
        public readonly List<RectangleF> VisibleImages = new List<RectangleF>();
        public readonly List<List<VectorPath>> VisibleFills = new List<List<VectorPath>>();
        public RectangleF Bounds;
        public bool HasBounds;
        public bool Visible { get { return VisiblePaths.Count > 0 || VisibleImages.Count > 0; } }
        internal void Include(RectangleF bounds) { Bounds = HasBounds ? RectangleF.Union(Bounds, bounds) : bounds; HasBounds = true; }
        public void Move(double dx, double dy)
        {
            foreach (var layer in Layers) { layer.TranslateX += dx; layer.TranslateY += dy; }
            foreach (var item in Objects)
                AffineTransform.WorldPath(item, AffineTransform.Translation(dx,dy));
        }
        public bool IsSelected(CanvasSelectionState selection)
        {
            return Layers.Any(l => selection.Layers.Contains(l.Id)) || Objects.Any(o => selection.Paths.Contains(o.Path.Id));
        }
    }

    public static class CanvasLayout
    {
        public static List<CanvasUnit> Units(LaserDocument document, bool objects, bool geometry = true)
        {
            var units = new List<CanvasUnit>(); if (document == null) return units;
            var byId = new Dictionary<Guid, CanvasUnit>();
            Func<Guid, string, CanvasUnit> get = (id, name) =>
            {
                CanvasUnit unit;
                if (!byId.TryGetValue(id, out unit))
                {
                    var group = document.Groups.FirstOrDefault(g => g.Id == id);
                    string label = (group == null ? name : group.Name) ?? "";
                    unit = new CanvasUnit { Id = id, Name = label.Length > 60 ? label.Substring(0,57) + "..." : label }; byId.Add(id, unit); units.Add(unit);
                }
                return unit;
            };
            foreach (var layer in document.Layers.OrderBy(l => l.Order))
            {
                Guid layerRoot = CanvasGroups.Root(document, layer.GroupId);
                if (!objects || layer.ContentKind == LayerContentKind.Raster)
                {
                    var unit = get(layerRoot == Guid.Empty ? layer.Id : layerRoot, layer.Name);
                    unit.Layers.Add(layer);
                    RectangleF bounds;
                    if (LayerGeometry.TryGetBounds(layer, out bounds)) unit.Include(bounds);
                    if (layer.Visible && geometry)
                    {
                        if (layer.ContentKind == LayerContentKind.Raster) { if (layer.HasContent) unit.VisibleImages.Add(bounds); }
                        else
                        {
                            var paths = layer.VectorItems.SelectMany(i => i.Paths).Where(p => p.Points.Count > 1).Select(p => LayerGeometry.Transform(p, layer)).ToList();
                            unit.VisiblePaths.AddRange(paths);
                            if (layer.Mode == LayerMode.Fill || layer.Mode == LayerMode.OffsetFill) unit.VisibleFills.Add(paths);
                        }
                    }
                    continue;
                }
                foreach (var item in layer.VectorItems)
                    foreach (var path in item.Paths.Where(p => p.Points.Count > 1))
                    {
                        Guid root = CanvasGroups.Root(document, path.GroupId);
                        Guid id = root != Guid.Empty ? root : item.Text != null ? item.Id : path.Id;
                        var unit = get(id, item.Text == null ? layer.Name : item.Text.Content.Replace("\r", " ").Replace("\n", " "));
                        unit.Objects.Add(new VectorSelection(layer, item, path));
                        unit.Include(LayerGeometry.TransformedBounds(path, layer));
                        if (layer.Visible && geometry) unit.VisiblePaths.Add(LayerGeometry.Transform(path, layer));
                    }
            }
            // Fill parity belongs to each source layer, including holes in text.
            if (objects && geometry)
                foreach (var unit in units)
                    foreach (var layer in unit.Objects.Select(o => o.Layer).Distinct().Where(l => l.Visible && (l.Mode == LayerMode.Fill || l.Mode == LayerMode.OffsetFill)))
                        foreach (var item in unit.Objects.Where(o => o.Layer == layer).GroupBy(o => o.Item))
                            if (item.Count() == item.Key.Paths.Count) unit.VisibleFills.Add(item.Select(o => LayerGeometry.Transform(o.Path, layer)).ToList());
            return units.Where(u => u.HasBounds).ToList();
        }

        public static bool Align(LaserDocument document, CanvasSelectionState selection, CanvasAlignmentAction action, RectangleF? table)
        {
            var selected = Units(document, selection.Objects, false).Where(u => u.IsSelected(selection)).ToList();
            if (selected.Count == 0) return false;
            var reference = selected.FirstOrDefault(u => u.Id == selection.ReferenceId) ?? selected.Last();
            RectangleF target = table ?? reference.Bounds;
            RectangleF block = selected.Select(u => u.Bounds).Aggregate(RectangleF.Union);
            bool changed = false;
            foreach (var unit in selected)
            {
                if (!table.HasValue && unit == reference) continue;
                var bounds = table.HasValue ? block : unit.Bounds;
                double dx = 0, dy = 0;
                switch (action)
                {
                    case CanvasAlignmentAction.Left: dx = target.Left - bounds.Left; break;
                    case CanvasAlignmentAction.Right: dx = target.Right - bounds.Right; break;
                    case CanvasAlignmentAction.Top: dy = target.Bottom - bounds.Bottom; break;
                    case CanvasAlignmentAction.Bottom: dy = target.Top - bounds.Top; break;
                    case CanvasAlignmentAction.HorizontalCenter: dx = (target.Left + target.Right - bounds.Left - bounds.Right) / 2; break;
                    case CanvasAlignmentAction.VerticalCenter: dy = (target.Top + target.Bottom - bounds.Top - bounds.Bottom) / 2; break;
                    case CanvasAlignmentAction.Center:
                        dx = (target.Left + target.Right - bounds.Left - bounds.Right) / 2;
                        dy = (target.Top + target.Bottom - bounds.Top - bounds.Bottom) / 2; break;
                }
                if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6) continue;
                unit.Move(dx, dy); changed = true;
            }
            return changed;
        }

        public static RectangleF SelectionRectangle(PointF start, PointF end)
        { return RectangleF.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)); }
        public static bool Crossing(PointF start, PointF end) { return end.Y < start.Y; }
        private static bool Contains(RectangleF r, LaserPoint p)
        { const double epsilon = 1e-6; return p.X >= r.Left - epsilon && p.X <= r.Right + epsilon && p.Y >= r.Top - epsilon && p.Y <= r.Bottom + epsilon; }
        private static bool SegmentTouches(RectangleF r, LaserPoint a, LaserPoint b)
        {
            double lo = 0, hi = 1, dx = b.X - a.X, dy = b.Y - a.Y;
            double[] p = { -dx, dx, -dy, dy }, q = { a.X-r.Left, r.Right-a.X, a.Y-r.Top, r.Bottom-a.Y };
            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-12) { if (q[i] < -1e-6) return false; continue; }
                double t = q[i] / p[i]; if (p[i] < 0) lo = Math.Max(lo, t); else hi = Math.Min(hi, t);
                if (lo > hi + 1e-9) return false;
            }
            return true;
        }
        public static bool Matches(CanvasUnit unit, RectangleF rectangle, bool crossing, Func<PointF, PointF> toClient)
        {
            var paths = unit.VisiblePaths.Select(p => new VectorPath { Closed = p.Closed, Points = p.Points.Select(v => { var q = toClient(new PointF((float)v.X, (float)v.Y)); return new LaserPoint(q.X, q.Y); }).ToList() }).ToList();
            var images = unit.VisibleImages.Select(r => SelectionRectangle(toClient(new PointF(r.Left, r.Top)), toClient(new PointF(r.Right, r.Bottom)))).ToList();
            if (!unit.Visible) return false;
            if (!crossing)
                return paths.All(p => p.Points.All(v => Contains(rectangle, v))) && images.All(r => Contains(rectangle, new LaserPoint(r.Left, r.Top)) && Contains(rectangle, new LaserPoint(r.Right, r.Bottom)));
            foreach (var path in paths)
            {
                if (path.Points.Any(p => Contains(rectangle, p))) return true;
                int count = path.Closed ? path.Points.Count : path.Points.Count - 1;
                for (int i = 0; i < count; i++) if (SegmentTouches(rectangle, path.Points[i], path.Points[(i + 1) % path.Points.Count])) return true;
            }
            if (images.Any(r => r.Left <= rectangle.Right && r.Right >= rectangle.Left && r.Top <= rectangle.Bottom && r.Bottom >= rectangle.Top)) return true;
            foreach (var fill in unit.VisibleFills)
            {
                using (var polygon = new GraphicsPath(FillMode.Alternate))
                {
                    foreach (var path in fill.Where(p => p.Closed && p.Points.Count > 2)) polygon.AddPolygon(path.Points.Select(p => toClient(new PointF((float)p.X, (float)p.Y))).ToArray());
                    if (polygon.PointCount > 0 && polygon.IsVisible(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2)) return true;
                }
            }
            return false;
        }
    }

    public static class CanvasOverlay
    {
        public static void Draw(Graphics graphics, RectangleF? marquee, bool crossing, RectangleF? reference)
        {
            if (marquee.HasValue)
            {
                var r = marquee.Value;
                using (var shade = new SolidBrush(Color.FromArgb(25, ColorScheme.BrandAmber))) graphics.FillRectangle(shade, r);
                using (var pen = new Pen(ColorScheme.FocusRing, 1.5f)) { pen.DashStyle = crossing ? DashStyle.Dash : DashStyle.Solid; graphics.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height); }
                using (var brush = new SolidBrush(ColorScheme.FormForeColor)) graphics.DrawString(LayerText.Get(crossing ? "SelectCrossing" : "SelectEnclosed"), SystemFonts.MessageBoxFont, brush, r.Left + 4, r.Top + 4);
            }
            if (reference.HasValue)
            {
                var r = reference.Value;
                using (var pen = new Pen(ColorScheme.FocusRing, 2))
                {
                    graphics.DrawEllipse(pen, r.Left - 4, r.Top - 4, 8, 8);
                    graphics.DrawLine(pen, r.Left - 7, r.Top, r.Left + 7, r.Top);
                    graphics.DrawLine(pen, r.Left, r.Top - 7, r.Left, r.Top + 7);
                }
            }
        }
    }
}
