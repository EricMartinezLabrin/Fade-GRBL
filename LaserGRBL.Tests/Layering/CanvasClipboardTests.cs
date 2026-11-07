using System;
using System.Drawing;
using System.Linq;
using LaserGRBL.Layering;
using Xunit;

namespace LaserGRBL.Tests.Layering
{
    public class CanvasClipboardTests
    {
        private static LaserLayer Vector(string name, Color color)
        {
            return new LaserLayer { Name = name, Color = color, TranslateX = 10, TranslateY = 20, ScaleX = 2, ScaleY = 3,
                SpeedMmPerMinute = 1234, PowerPercent = 42, Passes = 3,
                VectorItems = { new VectorItem { SourceName = name, Paths = {
                    new VectorPath { Closed = true, Points = { new LaserPoint(1, 2), new LaserPoint(4, 2), new LaserPoint(4, 6) } },
                    new VectorPath { Points = { new LaserPoint(8, 9), new LaserPoint(11, 9) } } } } } };
        }
        [Fact]
        public void Paste_MultipleColors_MergesIntoDestinationAndOffsetsInMachineCoordinates()
        {
            var a = Vector("A", Color.Red); var b = Vector("B", Color.Blue); b.Visible = false;
            var document = new LaserDocument { Layers = { a, b } };
            var clipboard = new CanvasClipboard(); clipboard.CopyLayers(document, new[] { b, a, a });
            a.PowerPercent = 17;
            var originals = a.VectorItems[0].Paths.Select(p => LayerGeometry.Transform(p, a).Points[0]).ToList();
            var first = clipboard.Paste(document, false);
            Assert.Equal(new[] { a, b }, first); Assert.Equal(2, document.Layers.Count);
            Assert.True(a.OutputEnabled); Assert.Equal(17, a.PowerPercent); Assert.False(b.Visible);
            Assert.Equal(originals[0].X + 5, LayerGeometry.Transform(a.VectorItems[1].Paths[0], a).Points[0].X);
            Assert.Equal(originals[0].Y + 5, LayerGeometry.Transform(a.VectorItems[1].Paths[0], a).Points[0].Y, 6);
            a.VectorItems[1].Paths[0].Points[0].X = 777;
            clipboard.Paste(document, false);
            Assert.Equal(originals[0].X + 10, LayerGeometry.Transform(a.VectorItems[2].Paths[0], a).Points[0].X);
            Assert.NotEqual(a.VectorItems[0].Id, a.VectorItems[2].Id);
            Assert.NotEqual(a.VectorItems[0].Paths[0].Id, a.VectorItems[2].Paths[0].Id);
        }
        [Fact]
        public void Paste_SelectedObjects_OnlyCopiesChosenContoursAndPreservesSourceSnapshot()
        {
            var layer = Vector("A", Color.Red); var document = new LaserDocument { Layers = { layer } };
            var clipboard = new CanvasClipboard();
            clipboard.CopyObjects(document, new[] { new VectorSelection(layer, layer.VectorItems[0], layer.VectorItems[0].Paths[1]) });
            double original = LayerGeometry.Transform(layer.VectorItems[0].Paths[1], layer).Points[0].X;
            layer.VectorItems[0].Paths[1].Points[0].X = 900;
            var destination = clipboard.Paste(document, false).Single();
            Assert.Same(layer, destination); Assert.True(clipboard.ContainsObjects);
            Assert.Single(layer.VectorItems[1].Paths);
            Assert.Equal(original + 5, LayerGeometry.Transform(layer.VectorItems[1].Paths[0], layer).Points[0].X);
        }
        [Fact]
        public void Paste_Raster_RemainsIndependentAndDeepCopiesData()
        {
            var layer = new LaserLayer { ContentKind = LayerContentKind.Raster, Mode = LayerMode.Image,
                RasterItem = new RasterItem { ImageData = new byte[] { 1, 2 }, BaseCommands = { "G0 X0 Y0", "M4 S100", "G1 X10 Y10 F1000", "M5" }, Settings = { RasterSetting.From("example", 42) } } };
            var document = new LaserDocument { Layers = { layer } }; var clipboard = new CanvasClipboard(); clipboard.CopyLayers(document, new[] { layer });
            var copy = clipboard.Paste(document, false).Single();
            Assert.False(copy.OutputEnabled); Assert.Equal(2, document.Layers.Count);
            copy.RasterItem.ImageData[0] = 9; copy.RasterItem.BaseCommands.Clear(); copy.RasterItem.Settings[0].Value = "0";
            Assert.Equal(1, layer.RasterItem.ImageData[0]); Assert.Equal(4, layer.RasterItem.BaseCommands.Count); Assert.Equal("42", layer.RasterItem.Settings[0].Value);
            Assert.Equal(1, clipboard.Paste(document, false).Single().RasterItem.ImageData[0]);
        }
        [Fact]
        public void Paste_Locked_DoesNotConsumeOffset()
        {
            var layer = Vector("A", Color.Red); var document = new LaserDocument { Layers = { layer } }; var clipboard = new CanvasClipboard();
            Assert.Empty(clipboard.Paste(document, false)); clipboard.CopyLayers(document, new[] { layer });
            Assert.Empty(clipboard.Paste(document, true)); Assert.Single(layer.VectorItems);
            clipboard.Paste(document, false);
            Assert.Equal(2.5, layer.VectorItems[1].Paths[0].Points[0].X - layer.VectorItems[0].Paths[0].Points[0].X);
            clipboard.Clear(); Assert.False(clipboard.HasContent);
        }
        [Fact]
        public void Paste_Cut_FirstPasteKeepsPositionThenOffsets()
        {
            var layer = Vector("A", Color.Red); var document = new LaserDocument { Layers = { layer } }; var clipboard = new CanvasClipboard();
            clipboard.CopyLayers(document, new[] { layer }); clipboard.MarkCut(); document.Layers.Clear();
            var first = clipboard.Paste(document, false).Single();
            Assert.Equal(10, first.TranslateX); Assert.Equal(20, first.TranslateY);
            clipboard.Paste(document, false);
            Assert.Equal(2.5, first.VectorItems[1].Paths[0].Points[0].X - first.VectorItems[0].Paths[0].Points[0].X);
        }
        [Fact]
        public void Paste_RoundTrip_SameColorCopiesUseDestinationOutputAndSettings()
        {
            var layer = Vector("A", Color.Red); var document = new LaserDocument { Layers = { layer } }; var clipboard = new CanvasClipboard(); clipboard.CopyLayers(document, new[] { layer });
            var original = LayeredJobCompiler.Compile(document, 1000, "M4", "M5"); clipboard.Paste(document, false);
            string filename = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".lps");
            try
            {
                LayerProjectSerializer.Save(filename, document); var restored = LayerProjectSerializer.Load(filename);
                Assert.Single(restored.Layers); Assert.Equal(2, restored.Layers[0].VectorItems.Count);
                var compiled = LayeredJobCompiler.Compile(restored, 1000, "M4", "M5");
                Assert.False(compiled.HasErrors); Assert.True(compiled.OutputCommands.Count > original.OutputCommands.Count);
                Assert.All(compiled.OutputCommands, command => Assert.Equal(layer.Id, command.SourceLayerId));
            }
            finally { System.IO.File.Delete(filename); }
        }
    }
}
