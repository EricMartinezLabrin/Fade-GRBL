using LaserGRBL;
using LaserGRBL.Layering;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LaserGRBL.Tests.Layering
{
    public class ObjectPassOrderTests
    {
        private static VectorPath Polygon(params double[] points)
        {
            var path = new VectorPath { Closed = true };
            for (int i = 0; i < points.Length; i += 2) path.Points.Add(new LaserPoint(points[i], points[i + 1]));
            return path;
        }
        private static VectorPath Circle()
        {
            var path = new VectorPath { Closed = true };
            for (int i = 0; i < 64; i++) path.Points.Add(new LaserPoint(20 + 20 * Math.Cos(i * Math.PI / 32), 20 + 20 * Math.Sin(i * Math.PI / 32)));
            return path;
        }
        private static LaserDocument Example(JobPassOrder order, int passes = 10)
        {
            return new LaserDocument { PassOrder = order, Layers = { new LaserLayer { Passes = passes, VectorItems = {
                new VectorItem { Paths = { Circle(), Polygon(10, 15, 16, 15, 13, 22), Polygon(22, 15, 28, 15, 28, 21, 22, 21) } }
            } } } };
        }
        private static LayeredCompilation Compile(LaserDocument document)
        {
            var result = LayeredJobCompiler.Compile(document, 1000, "M4", "M5"); Assert.False(result.HasErrors); return result;
        }

        [Fact]
        public void ByObject_TriangleAndSquareInsideCircle_CompletesTenPassesEachBeforeOuter()
        {
            var result = Compile(Example(JobPassOrder.ByObject));
            var markers = result.OutputCommands.Where(c => c.IsJobCheckpoint).ToList();
            Assert.Equal(Enumerable.Repeat(1, 10).Concat(Enumerable.Repeat(2, 10)).Concat(Enumerable.Repeat(0, 10)), markers.Select(c => c.SourceObjectIndex));
            Assert.Equal(Enumerable.Range(1, 10).Concat(Enumerable.Range(1, 10)).Concat(Enumerable.Range(1, 10)), markers.Select(c => c.SourcePass));
            Assert.Equal(30, result.OutputCommands.Count(c => c.Command == "M4 S1000"));
            Assert.All(markers, c => Assert.Equal(-1, c.SourceCommandIndex));
            foreach (var group in result.OutputCommands.Where(c => !c.IsJobCheckpoint).GroupBy(c => new { c.SourceObjectIndex, c.SourcePass }))
                Assert.Equal(Enumerable.Range(0, group.Count()), group.Select(c => c.SourceCommandIndex));
        }

        [Fact]
        public void ByObject_GlobalCounterTen_RepeatsEachObjectTenTimesWithoutRepeatingWholeJob()
        {
            var document = Example(JobPassOrder.ByObject, 1);
            var result = LayeredJobCompiler.Compile(document, 1000, "M4", "M5", 10);
            Assert.False(result.HasErrors);
            var markers = result.OutputCommands.Where(c => c.IsJobCheckpoint).ToList();
            Assert.Equal(Enumerable.Repeat(1, 10).Concat(Enumerable.Repeat(2, 10)).Concat(Enumerable.Repeat(0, 10)), markers.Select(c => c.SourceObjectIndex));
            Assert.Equal(Enumerable.Range(1, 10).Concat(Enumerable.Range(1, 10)).Concat(Enumerable.Range(1, 10)), markers.Select(c => c.SourcePass));
            var session = new JobExecutionSession(result.OutputCommands, document, 10, 10);
            session.SetCyclesRemaining(10);
            Assert.Equal(1, session.Cycles); Assert.Equal(10, session.Snapshot().Passes);
            Assert.Equal(1, document.Layers[0].Passes);
        }

        [Fact]
        public void ByObject_LayerPassesAndCounter_MultiplyWithoutChangingLayerConfiguration()
        {
            var document = Example(JobPassOrder.ByObject, 2);
            var result = LayeredJobCompiler.Compile(document, 1000, "M4", "M5", 3);
            var markers = result.OutputCommands.Where(c => c.IsJobCheckpoint).ToList();
            Assert.Equal(Enumerable.Repeat(1, 6).Concat(Enumerable.Repeat(2, 6)).Concat(Enumerable.Repeat(0, 6)), markers.Select(c => c.SourceObjectIndex));
            Assert.Equal(Enumerable.Range(1, 6), markers.Take(6).Select(c => c.SourcePass));
            Assert.Equal(2, document.Layers[0].Passes);
        }

        [Fact]
        public void ByLayer_Example_KeepsAllObjectsInEveryPass()
        {
            var result = Compile(Example(JobPassOrder.ByLayer));
            Assert.Equal(Enumerable.Range(1, 10), result.OutputCommands.Where(c => c.IsJobCheckpoint).Select(c => c.SourcePass));
            Assert.All(result.OutputCommands, c => Assert.Equal(-1, c.SourceObjectIndex));
            foreach (var pass in result.OutputCommands.GroupBy(c => c.SourcePass)) Assert.Equal(3, pass.Count(c => c.Command == "M4 S1000"));
        }

        [Fact]
        public void ByObject_InnerInLaterTransformedLayer_OverridesLayerOrderAndKeepsParameters()
        {
            var outer = new LaserLayer { Passes = 2, VectorItems = { new VectorItem { Paths = { Polygon(0, 0, 40, 0, 40, 40, 0, 40) } } } };
            var inner = new LaserLayer { Order = 1, Passes = 3, PowerPercent = 25, SpeedMmPerMinute = 432, ScaleX = 2, ScaleY = 3, TranslateX = 10, TranslateY = 10,
                Visible = false, VectorItems = { new VectorItem { Paths = { Polygon(0, 0, 3, 0, 3, 3, 0, 3) } } } };
            var document = new LaserDocument { PassOrder = JobPassOrder.ByObject, Layers = { outer, inner } };
            var result = Compile(document);
            Assert.Equal(Enumerable.Repeat(inner.Id, 3).Concat(Enumerable.Repeat(outer.Id, 2)), result.OutputCommands.Where(c => c.IsJobCheckpoint).Select(c => c.SourceLayerId.Value));
            Assert.Equal(3, result.OutputCommands.Count(c => c.Command == "M4 S250"));
            Assert.Equal(3, result.OutputCommands.Count(c => c.Command == "F432"));
            Assert.Contains(result.OutputCommands, c => c.Command == "G0 X10 Y10");
            Assert.DoesNotContain(result.PreviewCommands, c => c.SourceLayerId == inner.Id);
            inner.OutputEnabled = false;
            Assert.DoesNotContain(Compile(document).OutputCommands, c => c.SourceLayerId == inner.Id);
        }

        [Fact]
        public void ByObject_ConcaveBoundaryCrossingAndTouching_AreNotTreatedAsContained()
        {
            var document = Example(JobPassOrder.ByObject, 1); var paths = document.Layers[0].VectorItems[0].Paths; paths.Clear();
            paths.Add(Polygon(0, 0, 10, 0, 10, 10, 7, 10, 7, 3, 3, 3, 3, 10, 0, 10));
            var crossing = Polygon(1, 8, 9, 8); crossing.Closed = false; paths.Add(crossing);
            Assert.Equal(new[] { 0, 1 }, Compile(document).OutputCommands.Where(c => c.IsJobCheckpoint).Select(c => c.SourceObjectIndex));
            paths.Clear(); paths.Add(Polygon(0, 0, 10, 0, 10, 10, 0, 10)); paths.Add(Polygon(0, 2, 2, 2, 2, 4));
            Assert.Equal(new[] { 0, 1 }, Compile(document).OutputCommands.Where(c => c.IsJobCheckpoint).Select(c => c.SourceObjectIndex));
        }

        [Fact]
        public void ByObject_ThreeNestedContours_DeepestFirstAndNoAreaSortForDisjointPaths()
        {
            var document = Example(JobPassOrder.ByObject, 1); var paths = document.Layers[0].VectorItems[0].Paths; paths.Clear();
            paths.Add(Polygon(0, 0, 30, 0, 30, 30, 0, 30)); paths.Add(Polygon(5, 5, 25, 5, 25, 25, 5, 25)); paths.Add(Polygon(10, 10, 20, 10, 20, 20, 10, 20));
            Assert.Equal(new[] { 2, 1, 0 }, Compile(document).OutputCommands.Where(c => c.IsJobCheckpoint).Select(c => c.SourceObjectIndex));
            paths[2] = Polygon(40, 0, 41, 0, 41, 1, 40, 1);
            Assert.Equal(new[] { 1, 0, 2 }, Compile(document).OutputCommands.Where(c => c.IsJobCheckpoint).Select(c => c.SourceObjectIndex));
        }

        [Fact]
        public void ByObject_SinglePassPause_RewindStaysInCurrentObject()
        {
            var document = Example(JobPassOrder.ByObject, 1); var result = Compile(document);
            var session = new JobExecutionSession(result.OutputCommands, document, 1);
            foreach (var command in session.Commands) session.Sent(command);
            int second = session.Commands.FindIndex(c => c.IsJobCheckpoint && c.SourceObjectIndex == 2);
            session.Checkpoint(session.Commands[second]);
            session.SetState(JobExecutionState.Paused);
            Assert.Equal(second, session.PassStart); Assert.Equal(second, session.RewindTarget(1000));
            Assert.Equal(2, session.Snapshot().ObjectIndex); Assert.Equal(1, session.Snapshot().Pass);
        }

        [Theory]
        [InlineData(LayerMode.Fill)]
        [InlineData(LayerMode.OffsetFill)]
        [InlineData(LayerMode.Image)]
        public void ByObject_FillAndRaster_KeepWholeLayerPasses(LayerMode mode)
        {
            var document = Example(JobPassOrder.ByLayer, 2); var layer = document.Layers[0]; layer.Mode = mode;
            if (mode == LayerMode.Image)
            {
                layer.ContentKind = LayerContentKind.Raster;
                layer.RasterItem = new RasterItem { BaseCommands = { "G0 X0 Y0", "M4 S500", "G1 X10 Y0 F1000", "M5" } };
            }
            var before = Compile(document).OutputCommands;
            document.PassOrder = JobPassOrder.ByObject;
            var after = Compile(document).OutputCommands;
            Assert.Equal(before.Select(c => c.Command), after.Select(c => c.Command));
            Assert.All(after, c => Assert.Equal(-1, c.SourceObjectIndex));
        }

        [Fact]
        public void PassOrder_ProjectCloneAndRoundTrip_PreserveChoiceAndOldProjectDefaultsToLayer()
        {
            var document = Example(JobPassOrder.ByObject); Assert.Equal(JobPassOrder.ByObject, document.Clone().PassOrder);
            string filename = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".lps");
            try { LayerProjectSerializer.Save(filename, document); Assert.Equal(JobPassOrder.ByObject, LayerProjectSerializer.Load(filename).PassOrder); }
            finally { if (File.Exists(filename)) File.Delete(filename); }
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(LayerProjectSerializer.LaserProjectDto));
            using (var reader = new StringReader("<LaserProject version=\"2\"><Layers /></LaserProject>"))
                Assert.Equal(JobPassOrder.ByLayer, ((LayerProjectSerializer.LaserProjectDto)serializer.Deserialize(reader)).ToModel().PassOrder);
        }
    }
}
