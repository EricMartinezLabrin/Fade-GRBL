using ClipperLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LaserGRBL.Layering
{
    public class LayeredCompilation
    {
        public List<GrblCommand> OutputCommands { get; } = new List<GrblCommand>();
        public List<GrblCommand> PreviewCommands { get; } = new List<GrblCommand>();
        public bool HasErrors { get; set; }
    }

    public static class LayeredJobCompiler
    {
        private const double ClipperScale = 1000.0;
        private static readonly Regex FeedRegex = new Regex(@"(?<![A-Z])F-?\d+(?:\.\d+)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PowerRegex = new Regex(@"(?<![A-Z])S(-?\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static LayeredCompilation Compile(LaserDocument document, int maxPwm, string laserOn, string laserOff, int objectRepetitions = 1)
        {
            LayeredCompilation result = new LayeredCompilation();
            if (document == null) return result;
            if (document.PassOrder == JobPassOrder.ByObject) return CompileObjects(document, maxPwm, laserOn, laserOff, Math.Max(1, objectRepetitions));
            foreach (LaserLayer layer in document.Layers.OrderBy(l => l.Order))
            {
                layer.HasError = false;
                layer.StatusMessage = null;
                // Copies keep their source color. Layer identity and job metadata use Id.
                string validation = layer.Validate();
                if (validation != null)
                {
                    layer.HasError = layer.OutputEnabled;
                    layer.StatusMessage = validation;
                    result.HasErrors |= layer.OutputEnabled;
                    continue;
                }

                List<string> block;
                try { block = CompileLayer(layer, maxPwm, laserOn, laserOff); }
                catch (Exception ex)
                {
                    layer.HasError = layer.OutputEnabled;
                    layer.StatusMessage = ex.Message;
                    result.HasErrors |= layer.OutputEnabled;
                    continue;
                }

                if (block.Count == 0 && layer.OutputEnabled)
                {
                    layer.HasError = true;
                    layer.StatusMessage = "Layer has no valid geometry for selected mode.";
                    result.HasErrors = true;
                }

                if (layer.OutputEnabled)
                    result.OutputCommands.AddRange(ToCommands(block, layer, false));
                if (layer.Visible)
                    result.PreviewCommands.AddRange(ToCommands(block, layer, !layer.OutputEnabled));
            }
            return result;
        }

        private sealed class CutObject
        {
            public LaserLayer Layer;
            public int Index = -1;
            public VectorPath Path;
            public List<string> Commands;
        }

        private static LayeredCompilation CompileObjects(LaserDocument document, int maxPwm, string laserOn, string laserOff, int repetitions)
        {
            var result = new LayeredCompilation();
            var objects = new List<CutObject>();
            foreach (LaserLayer layer in document.Layers.OrderBy(l => l.Order))
            {
                layer.HasError = false; layer.StatusMessage = layer.Validate();
                if (layer.StatusMessage != null)
                {
                    layer.HasError = layer.OutputEnabled; result.HasErrors |= layer.HasError; continue;
                }
                var layerObjects = new List<CutObject>();
                try
                {
                    if (layer.ContentKind == LayerContentKind.Vector && layer.Mode == LayerMode.Line)
                    {
                        int index = 0;
                        foreach (VectorPath path in layer.VectorItems.SelectMany(item => item.Paths))
                        {
                            if (path.Points.Count > 1)
                            {
                                VectorPath transformed = LayerGeometry.Transform(path, layer);
                                layerObjects.Add(new CutObject { Layer = layer, Index = index, Path = transformed,
                                    Commands = CompileLayer(layer, maxPwm, laserOn, laserOff, transformed) });
                            }
                            index++;
                        }
                    }
                    else
                    {
                        // Raster and fill retain their whole-layer geometry (including holes).
                        layerObjects.Add(new CutObject { Layer = layer, Commands = CompileLayer(layer, maxPwm, laserOn, laserOff) });
                    }
                    if (!layerObjects.Any(item => item.Commands.Count > 0) && layer.OutputEnabled)
                        throw new InvalidOperationException("Layer has no valid geometry for selected mode.");
                    objects.AddRange(layerObjects);
                }
                catch (Exception ex)
                {
                    layer.StatusMessage = ex.Message; layer.HasError = layer.OutputEnabled; result.HasErrors |= layer.HasError;
                }
            }
            foreach (CutObject item in ObjectCutOrder.InsideFirst(objects.Where(item => item.Layer.OutputEnabled).ToList(), item => item.Path))
                result.OutputCommands.AddRange(ToCommands(Repeat(item.Commands, repetitions), item.Layer, false, item.Index, checked(item.Layer.Passes * repetitions)));
            foreach (CutObject item in ObjectCutOrder.InsideFirst(objects.Where(item => item.Layer.Visible).ToList(), item => item.Path))
                result.PreviewCommands.AddRange(ToCommands(Repeat(item.Commands, repetitions), item.Layer, !item.Layer.OutputEnabled, item.Index, checked(item.Layer.Passes * repetitions)));
            return result;
        }

        private static IEnumerable<GrblCommand> ToCommands(IEnumerable<string> lines, LaserLayer layer, bool muted, int objectIndex = -1, int totalPasses = 0)
        {
            List<string> materialized = lines.ToList();
            int perPass = materialized.Count / (totalPasses > 0 ? totalPasses : layer.Passes);
            int index = 0, movement = 0;
            decimal motion = 0, power = 0, x = 0, y = 0, z = 0;
            bool laser = false, absolute = true;
            foreach (string line in materialized)
            {
                if (perPass > 0 && index % perPass == 0)
                {
                    movement = 0;
                    // G4 synchronizes the GRBL planner before acknowledging this marker.
                    yield return new GrblCommand("G4 P0") { SourceLayerId = layer.Id,
                        SourceObjectIndex = objectIndex,
                        SourcePass = index / perPass + 1, SourceCommandIndex = -1,
                        IsJobCheckpoint = true, PreviewColor = layer.Color, PreviewMuted = muted };
                }
                GrblCommand command = new GrblCommand(line);
                command.SourceLayerId = layer.Id;
                command.SourceObjectIndex = objectIndex;
                command.SourcePass = perPass == 0 ? 1 : index / perPass + 1;
                command.SourceCommandIndex = perPass == 0 ? index : index % perPass;
                command.BuildHelper();
                if (command.IsAbsoluteCoord) absolute = true;
                if (command.IsRelativeCoord) absolute = false;
                if (command.G != null && command.G.Number >= 0 && command.G.Number <= 3) motion = command.G.Number;
                if (command.IsLaserON) laser = true;
                if (command.IsLaserOFF) laser = false;
                if (command.S != null) power = command.S.Number;
                decimal nx = command.X == null ? x : absolute ? command.X.Number : x + command.X.Number;
                decimal ny = command.Y == null ? y : absolute ? command.Y.Number : y + command.Y.Number;
                decimal nz = command.Z == null ? z : absolute ? command.Z.Number : z + command.Z.Number;
                command.IsCuttingMovement = laser && power > 0 && motion >= 1 && motion <= 3 &&
                    command.IsMovement && (nx != x || ny != y || nz != z || command.IsArcMovement);
                if (command.IsMovement) { x = nx; y = ny; z = nz; }
                if (command.IsCuttingMovement) movement++;
                command.SourceMovement = movement;
                command.DeleteHelper();
                command.PreviewColor = layer.Color;
                command.PreviewMuted = muted;
                index++;
                yield return command;
            }
        }

        private static List<string> CompileLayer(LaserLayer layer, int maxPwm, string laserOn, string laserOff, VectorPath objectPath = null)
        {
            if (!layer.HasContent) return new List<string>();
            if (layer.Mode == LayerMode.Image) return CompileRaster(layer, maxPwm);

            List<VectorPath> source = objectPath == null ? layer.VectorItems.SelectMany(i => i.Paths).Where(p => p.Points.Count > 1)
                .Select(path => LayerGeometry.Transform(path, layer)).ToList() : new List<VectorPath> { objectPath };
            List<VectorPath> paths;
            switch (layer.Mode)
            {
                case LayerMode.Fill: paths = BuildHatch(source.Where(p => p.Closed).ToList(), layer.FillSettings); break;
                case LayerMode.OffsetFill: paths = BuildOffsets(source.Where(p => p.Closed).ToList(), layer.FillSettings); break;
                default: paths = OrderLinePaths(source); break;
            }
            if ((layer.Mode == LayerMode.Fill || layer.Mode == LayerMode.OffsetFill) && source.Any(p => !p.Closed))
                layer.StatusMessage = "Open paths were skipped.";

            int power = (int)Math.Round(Math.Max(0, Math.Min(100, layer.PowerPercent)) * maxPwm / 100.0);
            List<string> onePass = new List<string>();
            onePass.Add(string.Format(CultureInfo.InvariantCulture, "F{0}", layer.SpeedMmPerMinute));
            foreach (VectorPath path in paths)
            {
                if (path.Points.Count < 2) continue;
                LaserPoint first = path.Points[0];
                onePass.Add(string.Format(CultureInfo.InvariantCulture, "G0 X{0:0.###} Y{1:0.###}", first.X, first.Y));
                onePass.Add(string.Format(CultureInfo.InvariantCulture, "{0} S{1}", laserOn, power));
                for (int i = 1; i < path.Points.Count; i++)
                    onePass.Add(string.Format(CultureInfo.InvariantCulture, "G1 X{0:0.###} Y{1:0.###}", path.Points[i].X, path.Points[i].Y));
                if (path.Closed && !SamePoint(path.Points[path.Points.Count - 1], first))
                    onePass.Add(string.Format(CultureInfo.InvariantCulture, "G1 X{0:0.###} Y{1:0.###}", first.X, first.Y));
                onePass.Add("S0");
            }
            if (onePass.Count > 1) onePass.Add(laserOff);
            return Repeat(onePass, layer.Passes);
        }

        private static List<string> CompileRaster(LaserLayer layer, int maxPwm)
        {
            RasterItem raster = layer.RasterItem;
            if (raster == null) return new List<string>();
            int desiredMax = (int)Math.Round(layer.PowerPercent * maxPwm / 100.0);
            int desiredMin = (int)Math.Round(Math.Max(0, Math.Min(layer.PowerPercent, raster.MinPowerPercent)) * maxPwm / 100.0);
            int originalMax = Math.Max(1, raster.OriginalMaxPower);
            int originalMin = Math.Max(0, Math.Min(originalMax, raster.OriginalMinPower));
            List<string> pass = new List<string>();
            foreach (string original in LayerGeometry.TransformRasterCommands(raster.BaseCommands, layer))
            {
                string line = FeedRegex.Replace(original, "F" + layer.SpeedMmPerMinute.ToString(CultureInfo.InvariantCulture));
                line = PowerRegex.Replace(line, m =>
                {
                    double oldValue;
                    if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out oldValue)) return m.Value;
                    double normalized = originalMax == originalMin ? 1 : Math.Max(0, Math.Min(1, (oldValue - originalMin) / (originalMax - originalMin)));
                    int scaled = oldValue <= 0 ? 0 : (int)Math.Round(desiredMin + normalized * (desiredMax - desiredMin));
                    return "S" + Math.Min(maxPwm, scaled).ToString(CultureInfo.InvariantCulture);
                });
                pass.Add(line);
            }
            return Repeat(pass, layer.Passes);
        }

        private static List<string> Repeat(List<string> commands, int passes)
        {
            List<string> result = new List<string>(commands.Count * passes);
            for (int i = 0; i < passes; i++) result.AddRange(commands);
            return result;
        }

        private static List<VectorPath> OrderLinePaths(List<VectorPath> paths)
        {
            return paths.OrderBy(p => p.Closed ? Math.Abs(SignedArea(p.Points)) : double.MaxValue).ToList();
        }

        private static List<VectorPath> BuildHatch(List<VectorPath> polygons, LayerFillSettings settings)
        {
            if (polygons.Count == 0) return new List<VectorPath>();
            double radians = -settings.AngleDegrees * Math.PI / 180.0;
            List<List<IntPoint>> clips = polygons.Select(p => Rotate(p.Points, radians).Select(ToIntPoint).ToList()).ToList();
            double minX = clips.SelectMany(p => p).Min(p => p.X) / ClipperScale;
            double maxX = clips.SelectMany(p => p).Max(p => p.X) / ClipperScale;
            double minY = clips.SelectMany(p => p).Min(p => p.Y) / ClipperScale;
            double maxY = clips.SelectMany(p => p).Max(p => p.Y) / ClipperScale;
            List<List<IntPoint>> lines = new List<List<IntPoint>>();
            int row = 0;
            for (double y = minY; y <= maxY + settings.IntervalMm / 2; y += settings.IntervalMm, row++)
            {
                List<IntPoint> line = new List<IntPoint> { ToIntPoint(new LaserPoint(minX - 1, y)), ToIntPoint(new LaserPoint(maxX + 1, y)) };
                if (settings.Bidirectional && row % 2 == 1) line.Reverse();
                lines.Add(line);
            }
            Clipper clipper = new Clipper();
            clipper.AddPaths(lines, PolyType.ptSubject, false);
            clipper.AddPaths(clips, PolyType.ptClip, true);
            PolyTree solution = new PolyTree();
            if (!clipper.Execute(ClipType.ctIntersection, solution, PolyFillType.pftEvenOdd, PolyFillType.pftEvenOdd)) return new List<VectorPath>();
            List<List<LaserPoint>> segments = Clipper.OpenPathsFromPolyTree(solution)
                .Where(p => p.Count > 1)
                .Select(p => p.Select(FromIntPoint).ToList())
                .OrderBy(p => p.Average(point => point.Y))
                .ThenBy(p => p.Min(point => point.X))
                .ToList();
            for (int i = 0; i < segments.Count; i++)
            {
                bool leftToRight = !settings.Bidirectional || i % 2 == 0;
                if ((segments[i][0].X <= segments[i][segments[i].Count - 1].X) != leftToRight)
                    segments[i].Reverse();
            }
            double back = -radians;
            return segments.Select(p => new VectorPath { Closed = false, Points = Rotate(p, back) }).ToList();
        }

        private static List<VectorPath> BuildOffsets(List<VectorPath> polygons, LayerFillSettings settings)
        {
            List<List<IntPoint>> subjects = polygons.Select(p => p.Points.Select(ToIntPoint).ToList()).Where(p => p.Count > 2).ToList();
            List<VectorPath> result = new List<VectorPath>();
            if (subjects.Count == 0) return result;
            Clipper normalizer = new Clipper();
            normalizer.AddPaths(subjects, PolyType.ptSubject, true);
            List<List<IntPoint>> normalized = new List<List<IntPoint>>();
            if (!normalizer.Execute(ClipType.ctUnion, normalized, PolyFillType.pftEvenOdd, PolyFillType.pftEvenOdd)) return result;
            ClipperOffset offsetter = new ClipperOffset();
            offsetter.AddPaths(normalized, JoinType.jtRound, EndType.etClosedPolygon);
            double step = settings.IntervalMm * ClipperScale;
            for (int iteration = 0; iteration < 100000; iteration++)
            {
                List<List<IntPoint>> solution = new List<List<IntPoint>>();
                offsetter.Execute(ref solution, -(step / 2.0 + iteration * step));
                if (solution.Count == 0) break;
                IEnumerable<List<IntPoint>> ordered = settings.OutsideToInside ? solution : solution.Reverse<List<IntPoint>>();
                result.AddRange(ordered.Where(p => p.Count > 2).Select(p => new VectorPath { Closed = true, Points = p.Select(FromIntPoint).ToList() }));
            }
            if (!settings.OutsideToInside) result.Reverse();
            return result;
        }

        private static List<LaserPoint> Rotate(IEnumerable<LaserPoint> points, double radians)
        {
            double cos = Math.Cos(radians), sin = Math.Sin(radians);
            return points.Select(p => new LaserPoint(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos)).ToList();
        }

        private static IntPoint ToIntPoint(LaserPoint p) { return new IntPoint(p.X * ClipperScale, p.Y * ClipperScale); }
        private static LaserPoint FromIntPoint(IntPoint p) { return new LaserPoint(p.X / ClipperScale, p.Y / ClipperScale); }
        private static bool SamePoint(LaserPoint a, LaserPoint b) { return Math.Abs(a.X - b.X) < 0.0001 && Math.Abs(a.Y - b.Y) < 0.0001; }
        private static double SignedArea(IList<LaserPoint> points)
        {
            double area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                LaserPoint a = points[i], b = points[(i + 1) % points.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            return area / 2.0;
        }
    }
}
