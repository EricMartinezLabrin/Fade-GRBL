using Svg;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;

namespace LaserGRBL.Layering
{
    public class SvgImportResult
    {
        public int ImportedElements { get; set; }
        public int SkippedElements { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class SvgLayerImporter
    {
        public static SvgImportResult Import(string filename, LaserDocument document, int defaultSpeed, double defaultPower)
        {
            return Import(SvgDocument.Open(filename), filename, document, defaultSpeed, defaultPower);
        }

        public static SvgImportResult Import(SvgDocument svg, string filename, LaserDocument document, int defaultSpeed, double defaultPower)
        {
            SvgImportResult result = new SvgImportResult();
            var groupIds = new Dictionary<SvgElement, Guid>();
            foreach (var node in svg.Descendants().Where(e => e is SvgGroup))
            {
                var group = new CanvasGroup { Name = string.IsNullOrEmpty(node.ID) ? LayerText.Get("GroupName") : node.ID };
                groupIds[node] = group.Id; document.Groups.Add(group);
            }
            foreach (var entry in groupIds)
            {
                var parent = entry.Key.Parents.FirstOrDefault(p => groupIds.ContainsKey(p));
                if (parent != null) document.Groups.First(g => g.Id == entry.Value).ParentId = groupIds[parent];
            }
            double ppi = svg.Ppi > 0 ? svg.Ppi : SvgDocument.PointsPerInch;
            double mmPerPixel = 25.4 / ppi;
            float documentWidth = svg.Width.ToDeviceValue(null, UnitRenderingType.Horizontal, svg);
            float documentHeight = svg.Height.ToDeviceValue(null, UnitRenderingType.Vertical, svg);
            if (documentWidth <= 0) documentWidth = svg.ViewBox.Width > 0 ? svg.ViewBox.Width : 100;
            if (documentHeight <= 0) documentHeight = svg.ViewBox.Height > 0 ? svg.ViewBox.Height : 100;

            IEnumerable<SvgVisualElement> elements = svg.Descendants().OfType<SvgVisualElement>()
                .Where(IsImportableLeaf);
            foreach (SvgVisualElement element in elements)
            {
                GraphicsPath graphicsPath = null;
                try
                {
                    graphicsPath = element.Path(null);
                    if (graphicsPath == null || graphicsPath.PointCount < 2) { result.SkippedElements++; continue; }
                    graphicsPath = (GraphicsPath)graphicsPath.Clone();
                    ApplyElementTransforms(graphicsPath, element);
                    ApplyViewBox(graphicsPath, svg, documentWidth, documentHeight);
                    graphicsPath.Flatten(null, (float)Math.Max(0.001, 0.05 / mmPerPixel));
                    Color color;
                    if (!TryGetColor(element, out color)) { result.SkippedElements++; continue; }
                    List<VectorPath> paths = ConvertPath(graphicsPath, documentHeight, mmPerPixel);
                    if (paths.Count == 0) { result.SkippedElements++; continue; }
                    var owner = element.Parents.FirstOrDefault(p => groupIds.ContainsKey(p));
                    if (owner != null) foreach (var path in paths) path.GroupId = groupIds[owner];

                    LaserLayer layer = document.FindByColor(color, LayerContentKind.Vector);
                    if (layer == null)
                    {
                        layer = new LaserLayer
                        {
                            Order = document.Layers.Count,
                            Name = "SVG " + ColorName(color),
                            Color = color,
                            ContentKind = LayerContentKind.Vector,
                            Mode = LayerMode.Line,
                            SpeedMmPerMinute = defaultSpeed,
                            PowerPercent = Math.Max(0, Math.Min(100, defaultPower))
                        };
                        document.Layers.Add(layer);
                    }
                    layer.VectorItems.Add(new VectorItem { SourceName = Path.GetFileName(filename), Paths = paths });
                    result.ImportedElements++;
                }
                catch (Exception ex)
                {
                    result.SkippedElements++;
                    if (result.Warnings.Count < 10) result.Warnings.Add(element.GetType().Name + ": " + ex.Message);
                }
                finally { if (graphicsPath != null) graphicsPath.Dispose(); }
            }
            document.NormalizeOrder();
            document.IsDirty = true;
            return result;
        }

        private static bool IsImportableLeaf(SvgVisualElement element)
        {
            if (!element.Visible || string.Equals(element.Display, "none", StringComparison.OrdinalIgnoreCase)) return false;
            string type = element.GetType().Name;
            if (type == "SvgGroup" || type == "SvgSymbol" || type == "SvgSwitch" || type == "SvgForeignObject" || type == "SvgImage") return false;
            return !element.Parents.OfType<SvgVisualElement>().Any(parent =>
            {
                string parentType = parent.GetType().Name;
                return parentType != "SvgGroup" && parentType != "SvgSymbol" && parentType != "SvgSwitch";
            });
        }

        private static void ApplyElementTransforms(GraphicsPath path, SvgElement element)
        {
            if (element.Transforms != null && element.Transforms.Count > 0) path.Transform(element.Transforms.GetMatrix());
            foreach (SvgElement parent in element.Parents)
                if (parent.Transforms != null && parent.Transforms.Count > 0) path.Transform(parent.Transforms.GetMatrix());
        }

        private static void ApplyViewBox(GraphicsPath path, SvgDocument svg, float width, float height)
        {
            if (svg.ViewBox.Width <= 0 || svg.ViewBox.Height <= 0) return;
            float scaleX = width / svg.ViewBox.Width;
            float scaleY = height / svg.ViewBox.Height;
            float scale = Math.Min(scaleX, scaleY);
            float offsetX = (width - svg.ViewBox.Width * scale) / 2f;
            float offsetY = (height - svg.ViewBox.Height * scale) / 2f;
            using (Matrix matrix = new Matrix())
            {
                matrix.Translate(-svg.ViewBox.MinX, -svg.ViewBox.MinY, MatrixOrder.Append);
                matrix.Scale(scale, scale, MatrixOrder.Append);
                matrix.Translate(offsetX, offsetY, MatrixOrder.Append);
                path.Transform(matrix);
            }
        }

        private static List<VectorPath> ConvertPath(GraphicsPath graphicsPath, double documentHeightPx, double mmPerPixel)
        {
            List<VectorPath> result = new List<VectorPath>();
            VectorPath current = null;
            PointF[] points = graphicsPath.PathPoints;
            byte[] types = graphicsPath.PathTypes;
            for (int i = 0; i < points.Length; i++)
            {
                byte kind = (byte)(types[i] & (byte)PathPointType.PathTypeMask);
                if (kind == (byte)PathPointType.Start)
                {
                    if (current != null && current.Points.Count > 1) result.Add(current);
                    current = new VectorPath();
                }
                if (current == null) current = new VectorPath();
                current.Points.Add(new LaserPoint(points[i].X * mmPerPixel, (documentHeightPx - points[i].Y) * mmPerPixel));
                if ((types[i] & (byte)PathPointType.CloseSubpath) != 0) current.Closed = true;
            }
            if (current != null && current.Points.Count > 1) result.Add(current);
            return result;
        }

        private static bool TryGetColor(SvgVisualElement element, out Color color)
        {
            color = Color.Black;
            SvgPaintServer server = element.Stroke;
            float opacity = element.StrokeOpacity * element.Opacity;
            if (server == null || server == SvgPaintServer.None || server == SvgColourServer.NotSet)
            {
                server = element.Fill;
                opacity = element.FillOpacity * element.Opacity;
            }
            if (opacity <= 0 || server == SvgPaintServer.None) return false;
            SvgColourServer colour = server as SvgColourServer;
            if (colour == null) return true;
            color = Color.FromArgb(255, colour.Colour.R, colour.Colour.G, colour.Colour.B);
            return colour.Colour.A > 0;
        }

        private static string ColorName(Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }
    }
}
