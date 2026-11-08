using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using LaserGRBL.Layering;

namespace LaserGRBL.Community
{
    public static class CommunitySvg
    {
        public const int MaximumBytes = 10000000;
        public static void Validate(string svg)
        {
            if (svg == null || Encoding.UTF8.GetByteCount(svg) > MaximumBytes) throw new InvalidDataException(CommunityText.Get("InvalidSvg"));
            var allowed = new HashSet<string>(new[] { "svg", "g", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "title", "desc" });
            int elements = 0;
            using (var reader = XmlReader.Create(new StringReader(svg), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumBytes }))
            {
                while (reader.Read())
                {
                    if (reader.Depth > 64) throw new InvalidDataException(CommunityText.Get("InvalidSvg"));
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    if (++elements > 50000 || !allowed.Contains(reader.LocalName) || reader.NamespaceURI != "http://www.w3.org/2000/svg") throw new InvalidDataException(CommunityText.Get("InvalidSvg"));
                    if (elements == 1 && reader.LocalName != "svg") throw new InvalidDataException(CommunityText.Get("InvalidSvg"));
                    if (reader.HasAttributes) while (reader.MoveToNextAttribute())
                    {
                        string name = reader.LocalName.ToLowerInvariant(), value = reader.Value.ToLowerInvariant();
                        if (name.StartsWith("on") || name == "href" || name == "style" || name == "src" || name == "base" || System.Text.RegularExpressions.Regex.IsMatch(value, @"url\s*\(") || value.Contains("javascript:") || value.Contains("data:")) throw new InvalidDataException(CommunityText.Get("InvalidSvg"));
                    }
                    reader.MoveToElement();
                }
            }
            if (elements == 0) throw new InvalidDataException(CommunityText.Get("InvalidSvg"));
        }
        public static LaserDocument Import(string svg)
        {
            Validate(svg);
            var document = new LaserDocument();
            var result = SvgLayerImporter.Import(Svg.SvgDocument.FromSvg<Svg.SvgDocument>(svg), "design.svg", document, 1000, 10);
            if (result.ImportedElements == 0 || result.SkippedElements > 0) throw new InvalidDataException(CommunityText.Get("InvalidSvg") + " (" + result.ImportedElements + "/" + result.SkippedElements + ") " + string.Join("; ", result.Warnings));
            var points = document.Layers.SelectMany(l => l.VectorItems).SelectMany(i => i.Paths).SelectMany(p => p.Points).ToList();
            if (points.Count > 1000000 || points.Any(p => double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y) || Math.Abs(p.X) > 1000000 || Math.Abs(p.Y) > 1000000)) throw new InvalidDataException(CommunityText.Get("InvalidSvg"));
            foreach (var layer in document.Layers) layer.OutputEnabled = false;
            return document;
        }
        private static string N(double value) { return value.ToString("0.######", CultureInfo.InvariantCulture); }
        public static string Export(LaserDocument document)
        {
            CommunityRuntime.DemandExport(document);
            if (document == null || document.Layers.Any(l => l.ContentKind == LayerContentKind.Raster)) throw new InvalidOperationException(CommunityText.Get("VectorOnly"));
            var paths = document.Layers.SelectMany(l => l.VectorItems.SelectMany(i => i.Paths.Select(p => new { layer = l, path = p, points = p.Points.Select(q => AffineTransform.Layer(l).Apply(q.X, q.Y)).ToList() }))).Where(p => p.points.Count > 1).ToList();
            if (paths.Count == 0 || paths.Sum(p => p.points.Count) > 1000000) throw new InvalidDataException(CommunityText.Get("InvalidSvg"));
            var points = paths.SelectMany(p => p.points).ToList();
            if (points.Any(p => double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y) || Math.Abs(p.X) > 1000000 || Math.Abs(p.Y) > 1000000)) throw new InvalidDataException(CommunityText.Get("InvalidSvg"));
            double left = points.Min(p => p.X), bottom = points.Min(p => p.Y), top = points.Max(p => p.Y);
            double width = Math.Max(.001, points.Max(p => p.X) - left), height = Math.Max(.001, top - bottom);
            var xml = new XmlDocument { XmlResolver = null };
            var root = xml.CreateElement("svg", "http://www.w3.org/2000/svg"); xml.AppendChild(root);
            root.SetAttribute("width", N(width) + "mm"); root.SetAttribute("height", N(height) + "mm"); root.SetAttribute("viewBox", "0 0 " + N(width) + " " + N(height));
            var groups = new Dictionary<Guid, XmlElement>(); var visiting = new HashSet<Guid>();
            Func<Guid, XmlElement> group = null;
            group = id =>
            {
                if (id == Guid.Empty) return root;
                XmlElement found; if (groups.TryGetValue(id, out found)) return found;
                var metadata = document.Groups.FirstOrDefault(g => g.Id == id);
                if (metadata == null) return root;
                if (!visiting.Add(id)) throw new InvalidDataException("Cyclic SVG group.");
                var parent = group(metadata.ParentId); var element = xml.CreateElement("g", root.NamespaceURI);
                element.SetAttribute("id", "g" + id.ToString("N")); parent.AppendChild(element); groups[id] = element; visiting.Remove(id); return element;
            };
            foreach (var item in paths)
            {
                var element = xml.CreateElement("path", root.NamespaceURI);
                element.SetAttribute("fill", "none"); element.SetAttribute("stroke", ColorTranslator.ToHtml(item.layer.Color)); element.SetAttribute("stroke-width", "0.1");
                var d = new StringBuilder();
                for (int i = 0; i < item.points.Count; i++) d.Append(i == 0 ? "M" : " L").Append(N(item.points[i].X - left)).Append(' ').Append(N(top - item.points[i].Y));
                if (item.path.Closed) d.Append(" Z"); element.SetAttribute("d", d.ToString()); group(item.path.GroupId).AppendChild(element);
            }
            string svg = xml.OuterXml; Validate(svg); return svg;
        }
        public static string Thumbnail(LaserDocument document)
        {
            using (var bitmap = new Bitmap(240, 160))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                var paths = document.Layers.SelectMany(l => l.VectorItems.SelectMany(i => i.Paths.Select(p => new { points = p.Points.Select(q => AffineTransform.Layer(l).Apply(q.X, q.Y)).ToList(), closed = p.Closed }))).Where(p => p.points.Count > 1).ToList();
                if (paths.Count > 0)
                {
                    var points = paths.SelectMany(p => p.points).ToList(); double x = points.Min(p => p.X), y = points.Min(p => p.Y);
                    double scale = Math.Min(220 / Math.Max(1, points.Max(p => p.X) - x), 140 / Math.Max(1, points.Max(p => p.Y) - y));
                    foreach (var path in paths)
                    {
                        var outline = path.points.Select(p => new PointF((float)(10 + (p.X - x) * scale), (float)(150 - (p.Y - y) * scale))).ToArray();
                        if (path.closed && outline.Length > 2) graphics.DrawPolygon(Pens.DimGray, outline); else graphics.DrawLines(Pens.DimGray, outline);
                    }
                }
                using (var memory = new MemoryStream()) { bitmap.Save(memory, ImageFormat.Png); return Convert.ToBase64String(memory.ToArray()); }
            }
        }
    }
}
