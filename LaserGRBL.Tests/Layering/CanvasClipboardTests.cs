using System;
using System.Linq;
using LaserGRBL.Layering;
using Xunit;

namespace LaserGRBL.Tests.Layering
{
    public class CanvasClipboardTests
    {
        private static LaserLayer Vector(string name)
        {
            return new LaserLayer
            {
                Name = name, TranslateX = 10, TranslateY = 20, ScaleX = 2, ScaleY = 3,
                SpeedMmPerMinute = 1234, PowerPercent = 42, Passes = 3,
                VectorItems = { new VectorItem { SourceName = name, Paths = {
                    new VectorPath { Closed = true, Points = { new LaserPoint(1, 2), new LaserPoint(4, 2), new LaserPoint(4, 6) } },
                    new VectorPath { Points = { new LaserPoint(8, 9), new LaserPoint(11, 9) } }
                } } }
            };
        }

        [Fact]
        public void Paste_MultipleLayers_UsesSnapshotAndCumulativeMachineOffsets()
        {
            var a = Vector("A"); var b = Vector("B"); b.Visible = false;
            var document = new LaserDocument { Layers = { a, b } }; document.NormalizeOrder();
            var clipboard = new CanvasClipboard(); clipboard.CopyLayers(document, new[] { b, a, a });
            a.VectorItems[0].Paths[0].Points[0].X = 999; document.Layers.Remove(a);
            var first = clipboard.Paste(document, false);
            Assert.Equal(new[] { "A", "B" }, first.Select(l => l.Name.Substring(0, 1)));
            Assert.All(first, l => { Assert.False(l.OutputEnabled); Assert.Equal(15, l.TranslateX); Assert.Equal(25, l.TranslateY); });
            Assert.False(first[1].Visible);
            Assert.Equal(1, first[0].VectorItems[0].Paths[0].Points[0].X);
            first[0].VectorItems[0].Paths[0].Points[0].X = 777;
            var second = clipboard.Paste(document, false);
            Assert.Equal(20, second[0].TranslateX); Assert.Equal(30, second[0].TranslateY);
            Assert.Equal(1, second[0].VectorItems[0].Paths[0].Points[0].X);
            Assert.NotEqual(first[0].Id, second[0].Id); Assert.NotEqual(a.Id, first[0].Id);
            Assert.Equal(Enumerable.Range(0, document.Layers.Count), document.Layers.Select(l => l.Order));
            Assert.True(document.IsDirty);
        }

        [Fact]
        public void Paste_ObjectsFromMultipleLayers_OnlyCopiesSelectedPathsAndPreservesTransforms()
        {
            var a = Vector("A"); var b = Vector("B"); b.TranslateX = -20;
            a.FillSettings.AngleDegrees = 45;
            var document = new LaserDocument { Layers = { a, b } }; document.NormalizeOrder();
            var clipboard = new CanvasClipboard();
            clipboard.CopyObjects(document, new[] {
                new VectorSelection(b, b.VectorItems[0], b.VectorItems[0].Paths[1]),
                new VectorSelection(a, a.VectorItems[0], a.VectorItems[0].Paths[0]) });
            var copies = clipboard.Paste(document, false);
            Assert.True(clipboard.ContainsObjects); Assert.Equal(2, copies.Count);
            Assert.All(copies, l => { Assert.Single(l.VectorItems); Assert.Single(l.VectorItems[0].Paths); Assert.False(l.OutputEnabled); });
            Assert.True(copies[0].VectorItems[0].Paths[0].Closed); Assert.False(copies[1].VectorItems[0].Paths[0].Closed);
            Assert.Equal(2, copies[0].ScaleX); Assert.Equal(3, copies[0].ScaleY);
            var original = LayerGeometry.Transform(a.VectorItems[0].Paths[0], a).Points[0];
            var copied = LayerGeometry.Transform(copies[0].VectorItems[0].Paths[0], copies[0]).Points[0];
            Assert.Equal(original.X + 5, copied.X); Assert.Equal(original.Y + 5, copied.Y);
            Assert.Equal(-15, copies[1].TranslateX);
            Assert.Equal(a.SpeedMmPerMinute, copies[0].SpeedMmPerMinute); Assert.Equal(a.PowerPercent, copies[0].PowerPercent);
            Assert.Equal(a.Passes, copies[0].Passes); Assert.Equal(a.ColorArgb, copies[0].ColorArgb);
            Assert.Equal(45, copies[0].FillSettings.AngleDegrees);
            copies[0].FillSettings.AngleDegrees = 0; Assert.Equal(45, a.FillSettings.AngleDegrees);
        }

        [Fact]
        public void Paste_Raster_DeepCopiesDataCommandsAndSettings()
        {
            var layer = new LaserLayer { ContentKind = LayerContentKind.Raster, Mode = LayerMode.Image,
                RasterItem = new RasterItem { ImageData = new byte[] { 1, 2 }, BaseCommands = { "G0 X0 Y0", "M4 S100", "G1 X10 Y10 F1000", "M5" },
                    Settings = { RasterSetting.From("example", 42) } } };
            var document = new LaserDocument { Layers = { layer } };
            var clipboard = new CanvasClipboard(); clipboard.CopyLayers(document, new[] { layer });
            var copy = clipboard.Paste(document, false).Single();
            copy.RasterItem.ImageData[0] = 9; copy.RasterItem.BaseCommands.Clear(); copy.RasterItem.Settings[0].Value = "0";
            Assert.Equal(1, layer.RasterItem.ImageData[0]); Assert.Equal(4, layer.RasterItem.BaseCommands.Count);
            Assert.Equal("42", layer.RasterItem.Settings[0].Value);
            var next = clipboard.Paste(document, false).Single();
            Assert.Equal(1, next.RasterItem.ImageData[0]); Assert.Equal(4, next.RasterItem.BaseCommands.Count);
        }

        [Fact]
        public void Paste_LockedOrEmpty_DoesNotChangeDocumentOrConsumeOffset()
        {
            var layer = Vector("A"); var document = new LaserDocument { Layers = { layer } };
            var clipboard = new CanvasClipboard();
            Assert.Empty(clipboard.Paste(document, false));
            clipboard.CopyLayers(document, new[] { layer });
            Assert.Empty(clipboard.Paste(document, true)); Assert.Single(document.Layers); Assert.False(document.IsDirty);
            Assert.Equal(15, clipboard.Paste(document, false).Single().TranslateX);
            clipboard.Clear(); Assert.False(clipboard.HasContent); Assert.Empty(clipboard.Paste(document, false));
        }

        [Fact]
        public void Paste_SaveReload_PreservesCopiesAndExcludesOutputUntilEnabled()
        {
            var layer = Vector("A"); var document = new LaserDocument { Layers = { layer } };
            var clipboard = new CanvasClipboard(); clipboard.CopyLayers(document, new[] { layer });
            var original = LayeredJobCompiler.Compile(document, 1000, "M4", "M5");
            clipboard.Paste(document, false);
            LaserDocument restored;
            string filename = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".lps");
            try { LayerProjectSerializer.Save(filename, document); restored = LayerProjectSerializer.Load(filename); }
            finally { if (System.IO.File.Exists(filename)) System.IO.File.Delete(filename); }
            Assert.Equal(2, restored.Layers.Count); Assert.False(restored.Layers[1].OutputEnabled);
            Assert.Equal(15, restored.Layers[1].TranslateX);
            var disabled = LayeredJobCompiler.Compile(restored, 1000, "M4", "M5");
            Assert.Equal(original.OutputCommands.Select(c => c.Command), disabled.OutputCommands.Select(c => c.Command));
            Assert.Contains(disabled.PreviewCommands, command => command.SourceLayerId == restored.Layers[1].Id);
            restored.Layers[1].OutputEnabled = true;
            var enabled = LayeredJobCompiler.Compile(restored, 1000, "M4", "M5");
            Assert.False(enabled.HasErrors); Assert.True(enabled.OutputCommands.Count > disabled.OutputCommands.Count);
        }
    }
}
