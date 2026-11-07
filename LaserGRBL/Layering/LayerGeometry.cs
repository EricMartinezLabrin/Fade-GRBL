using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LaserGRBL.Layering
{
    // A contour remains in its original layer, including compound SVG paths.
    public sealed class VectorSelection
    {
        public LaserLayer Layer { get; private set; }
        public VectorItem Item { get; private set; }
        public VectorPath Path { get; private set; }
        public VectorSelection(LaserLayer layer, VectorItem item, VectorPath path) { Layer = layer; Item = item; Path = path; }
        public bool IsCurrent(LaserDocument document) { return document != null && document.Layers.Contains(Layer) && Layer.VectorItems.Contains(Item) && Item.Paths.Contains(Path); }
        public RectangleF Bounds
        {
            get
            {
                return LayerGeometry.TransformedBounds(Path, Layer);
            }
        }
        public void MoveFrom(VectorPath original, double dx, double dy)
        {
            var local = AffineTransform.Layer(Layer).Inverse().Then(AffineTransform.Translation(dx,dy)).Then(AffineTransform.Layer(Layer));
            Path.Points = original.Points.Select(p => p.Clone()).ToList(); Path.Pose = original.Pose.Clone(); AffineTransform.Path(Path,local);
        }
        public void Delete()
        {
            Item.Paths.Remove(Path);
            if (Item.Paths.Count == 0) Layer.VectorItems.Remove(Item);
            if (!Layer.HasContent) Layer.OutputEnabled = false;
        }
    }

    public static class LayerGeometry
    {
        public static RectangleF TransformedBounds(VectorPath path, LaserLayer layer)
        {
            if (path.Points.Count == 0) return RectangleF.Empty;
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            foreach (var p in path.Points)
            {
                var q = AffineTransform.Layer(layer).Apply(p.X,p.Y); double x=q.X,y=q.Y;
                left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
            return RectangleF.FromLTRB((float)left,(float)top,(float)right,(float)bottom);
        }
        public static void DrawContours(Graphics graphics, IEnumerable<VectorPath> paths, Func<PointF, PointF> toClient)
        {
            using (Pen halo = new Pen(ColorScheme.DarkScheme ? Color.White : ColorScheme.BrandGraphite, 4.5f))
            using (Pen pen = new Pen(ColorScheme.SelectedContour, 2.5f))
                foreach (VectorPath path in paths)
                {
                    PointF[] points = path.Points.Select(p => toClient(new PointF((float)p.X, (float)p.Y))).ToArray();
                    if (points.Length < 2) continue;
                    if (ColorScheme.IsFade) graphics.DrawLines(halo, points);
                    graphics.DrawLines(pen, points);
                    if (path.Closed)
                    {
                        if (ColorScheme.IsFade) graphics.DrawLine(halo, points[points.Length - 1], points[0]);
                        graphics.DrawLine(pen, points[points.Length - 1], points[0]);
                    }
                }
        }

        public static void DrawSelectionBounds(Graphics graphics, RectangleF bounds)
        {
            using (Pen halo = new Pen(ColorScheme.DarkScheme ? Color.White : ColorScheme.BrandGraphite, 3.5f))
            using (Pen pen = new Pen(ColorScheme.BrandAmber, 1.5f))
            {
                if (ColorScheme.IsFade) graphics.DrawRectangle(halo, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
        }

        public static VectorSelection FindContour(LaserDocument document, PointF clientPoint, Func<PointF, PointF> toClient, double tolerance)
        {
            VectorSelection hit = null;
            double best = tolerance * tolerance;
            foreach (LaserLayer layer in document.Layers.Where(l => l.Visible && l.ContentKind == LayerContentKind.Vector))
                foreach (VectorItem item in layer.VectorItems)
                    foreach (VectorPath path in item.Paths.Where(p => p.Points.Count > 1))
                    {
                        List<PointF> points = Transform(path, layer).Points.Select(p => toClient(new PointF((float)p.X, (float)p.Y))).ToList();
                        int segments = path.Closed ? points.Count : points.Count - 1;
                        for (int i = 0; i < segments; i++)
                        {
                            PointF a = points[i], b = points[(i + 1) % points.Count];
                            double dx = b.X - a.X, dy = b.Y - a.Y;
                            double length = dx * dx + dy * dy;
                            double t = length == 0 ? 0 : Math.Max(0, Math.Min(1, ((clientPoint.X - a.X) * dx + (clientPoint.Y - a.Y) * dy) / length));
                            double x = clientPoint.X - a.X - t * dx, y = clientPoint.Y - a.Y - t * dy;
                            double distance = x * x + y * y;
                            if (distance <= best) { best = distance; hit = new VectorSelection(layer, item, path); }
                        }
                    }
            return hit;
        }

        private static readonly Regex AxisRegex = new Regex(@"(?<![A-Z])([XYIJ])(-?\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AbsoluteRegex = new Regex(@"(?<![A-Z])G90(?:\.0)?(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RelativeRegex = new Regex(@"(?<![A-Z])G91(?:\.0)?(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static VectorPath Transform(VectorPath path, LaserLayer layer)
        {
            return new VectorPath
            {
                Closed = path.Closed,
                Points = path.Points.Select(point => AffineTransform.Layer(layer).Apply(point.X,point.Y)).ToList()
            };
        }

        public static bool TryGetBounds(LaserLayer layer, out RectangleF bounds)
        {
            bounds = RectangleF.Empty;
            if (layer == null || !layer.HasContent) return false;
            List<LaserPoint> points = layer.ContentKind == LayerContentKind.Vector
                ? layer.VectorItems.SelectMany(item => item.Paths).SelectMany(path => path.Points).ToList()
                : ReadRasterPoints(layer.RasterItem == null ? null : layer.RasterItem.BaseCommands);
            if (points.Count == 0) return false;
            var world=points.Select(p=>AffineTransform.Layer(layer).Apply(p.X,p.Y)).ToList();
            bounds=RectangleF.FromLTRB((float)world.Min(p=>p.X),(float)world.Min(p=>p.Y),(float)world.Max(p=>p.X),(float)world.Max(p=>p.Y));
            return bounds.Width > 0.0001f || bounds.Height > 0.0001f;
        }

        public static bool TryGetBounds(IEnumerable<LaserLayer> layers, out RectangleF bounds)
        {
            bounds = RectangleF.Empty;
            bool found = false;
            foreach (LaserLayer layer in layers ?? Enumerable.Empty<LaserLayer>())
            {
                RectangleF layerBounds;
                if (!TryGetBounds(layer, out layerBounds)) continue;
                bounds = found ? RectangleF.Union(bounds, layerBounds) : layerBounds;
                found = true;
            }
            return found;
        }

        public static bool Resize(LaserLayer layer, double width, double height)
        {
            RectangleF bounds;
            if (!TryGetBounds(layer, out bounds) || width <= 0 || height <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return false;
            double ratioX = width / bounds.Width;
            double ratioY = height / bounds.Height;
            layer.ScaleX *= ratioX; layer.CrossX *= ratioX;
            layer.ScaleY *= ratioY; layer.CrossY *= ratioY;
            layer.TranslateX = bounds.Left + (layer.TranslateX - bounds.Left) * ratioX;
            layer.TranslateY = bounds.Top + (layer.TranslateY - bounds.Top) * ratioY;
            return true;
        }

        public static IEnumerable<string> TransformRasterCommands(IEnumerable<string> commands, LaserLayer layer)
        {
            return RasterAffine.Commands(commands, AffineTransform.Layer(layer));
        }

        public static List<LaserPoint> ReadRasterPoints(IEnumerable<string> commands)
        {
            List<LaserPoint> points = new List<LaserPoint>();
            if (commands == null) return points;
            bool absolute = true;
            double x = 0, y = 0;
            foreach (string command in RasterAffine.Commands(commands, new AffineTransform()))
            {
                string line = Regex.Replace(command ?? string.Empty, @";.*$|\([^)]*\)", string.Empty);
                if (AbsoluteRegex.IsMatch(line)) absolute = true;
                if (RelativeRegex.IsMatch(line)) absolute = false;
                double? nextX = null, nextY = null;
                foreach (Match match in AxisRegex.Matches(line))
                {
                    double value;
                    if (!double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) continue;
                    char axis = char.ToUpperInvariant(match.Groups[1].Value[0]);
                    if (axis == 'X') nextX = absolute ? value : x + value;
                    if (axis == 'Y') nextY = absolute ? value : y + value;
                }
                if (nextX.HasValue || nextY.HasValue)
                {
                    if (nextX.HasValue) x = nextX.Value;
                    if (nextY.HasValue) y = nextY.Value;
                    points.Add(new LaserPoint(x, y));
                }
            }
            return points;
        }
    }
}
