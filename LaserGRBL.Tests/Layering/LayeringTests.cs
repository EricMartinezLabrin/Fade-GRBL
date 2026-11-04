using LaserGRBL;
using LaserGRBL.Layering;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;

namespace LaserGRBL.Tests.Layering
{
    public class LayeringTests
    {
        [Theory]
        [InlineData("Speed", "1234")]
        [InlineData("Power", "55")]
        [InlineData("Passes", "12")]
        public void LayerGrid_TypingMultipleDigits_CommitsOnlyAtEnd(string column, string value)
        {
            RunSta(() =>
            {
                using (LayerManagerControl manager = new LayerManagerControl())
                {
                    var grid = (System.Windows.Forms.DataGridView)typeof(LayerManagerControl).GetField("grid", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(manager);
                    manager.CreateControl(); grid.CreateControl();
                    grid.Rows.Add(); grid.CurrentCell = grid.Rows[0].Cells[column];
                    Assert.True(grid.BeginEdit(false));
                    var editor = (System.Windows.Forms.TextBox)grid.EditingControl;
                    int committed = 0;
                    grid.CellValueChanged += (s, e) => committed++;
                    for (int i = 1; i <= value.Length; i++)
                    {
                        editor.Text = value.Substring(0, i);
                        grid.NotifyCurrentCellDirty(true);
                        typeof(LayerManagerControl).GetMethod("RefreshRows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(manager, null);
                        Assert.True(grid.IsCurrentCellInEditMode);
                        Assert.Equal(value.Substring(0, i), editor.Text);
                        Assert.Equal(0, committed);
                    }
                    Assert.True(grid.EndEdit());
                    Assert.Equal(value, Convert.ToString(grid.CurrentCell.Value));
                    Assert.Equal(1, committed);
                }
            });
        }

        [Theory]
        [InlineData("Speed", "0")]
        [InlineData("Power", "101")]
        [InlineData("Passes", "abc")]
        public void LayerGrid_InvalidValue_KeepsTextForCorrection(string column, string value)
        {
            RunSta(() =>
            {
                using (LayerManagerControl manager = new LayerManagerControl())
                {
                    var grid = (System.Windows.Forms.DataGridView)typeof(LayerManagerControl).GetField("grid", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(manager);
                    manager.CreateControl(); grid.CreateControl();
                    grid.Rows.Add(); grid.CurrentCell = grid.Rows[0].Cells[column];
                    Assert.True(grid.BeginEdit(false));
                    var editor = (System.Windows.Forms.TextBox)grid.EditingControl;
                    editor.Text = value;
                    grid.NotifyCurrentCellDirty(true);
                    var originalCell = grid.CurrentCell;
                    // Programmatic navigation throws when CellValidating cancels it.
                    Assert.Throws<InvalidOperationException>(() => grid.CurrentCell = grid.Rows[0].Cells["LayerName"]);
                    Assert.Same(originalCell, grid.CurrentCell);
                    Assert.True(grid.IsCurrentCellInEditMode);
                    Assert.Equal(value, editor.Text);
                    Assert.NotEmpty(grid.CurrentCell.ErrorText);
                    grid.CancelEdit();
                }
            });
        }

        private static void RunSta(Action action)
        {
            Exception failure = null;
            var thread = new System.Threading.Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start(); thread.Join();
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        [Fact]
        public void FindContour_CompoundPath_SelectsInnerContourAndIgnoresTravel()
        {
            LaserLayer layer = RectangleLayer();
            VectorPath inner = new VectorPath { Closed = true, Points = { new LaserPoint(2, 2), new LaserPoint(4, 2), new LaserPoint(4, 4), new LaserPoint(2, 4) } };
            layer.VectorItems[0].Paths.Add(inner);
            LaserDocument document = DocumentWith(layer);
            Assert.Same(inner, LayerGeometry.FindContour(document, new PointF(3, 2), p => p, 0.1).Path);
            Assert.Null(LayerGeometry.FindContour(document, new PointF(1, 1), p => p, 0.1));
            layer.Visible = false;
            Assert.Null(LayerGeometry.FindContour(document, new PointF(3, 2), p => p, 0.1));
        }

        [Fact]
        public void ObjectMoveAndDelete_PreserveSiblingAndPersistGeometry()
        {
            LaserLayer layer = RectangleLayer();
            layer.ScaleX = 2; layer.ScaleY = 3; layer.TranslateX = 8; layer.TranslateY = 9;
            VectorItem item = layer.VectorItems[0];
            VectorPath sibling = item.Paths[0].Clone(); item.Paths.Add(sibling);
            VectorSelection selection = new VectorSelection(layer, item, item.Paths[0]);
            double originalX = selection.Path.Points[0].X;
            double originalY = selection.Path.Points[0].Y;
            selection.MoveFrom(selection.Path.Clone(), 10, -12);
            Assert.Equal(originalX + 5, selection.Path.Points[0].X);
            Assert.Equal(originalY - 4, selection.Path.Points[0].Y);
            Assert.Equal(originalX, sibling.Points[0].X);
            LaserDocument copy = LayerProjectSerializer.LaserProjectDto.FromModel(DocumentWith(layer)).ToModel();
            Assert.Equal(originalX + 5, copy.Layers[0].VectorItems[0].Paths[0].Points[0].X);
            selection.Delete();
            Assert.Single(item.Paths); Assert.Same(sibling, item.Paths[0]);
            Assert.False(selection.IsCurrent(DocumentWith(layer)));
            new VectorSelection(layer, item, sibling).Delete();
            Assert.False(layer.HasContent); Assert.False(layer.OutputEnabled);
        }

        [Fact]
        public void Line_UsesLayerPowerSpeedAndPasses()
        {
            LaserLayer layer = RectangleLayer();
            layer.SpeedMmPerMinute = 1234;
            layer.PowerPercent = 50;
            layer.Passes = 2;
            LaserDocument document = DocumentWith(layer);

            LayeredCompilation result = LayeredJobCompiler.Compile(document, 1000, "M4", "M5");

            Assert.False(result.HasErrors);
            Assert.Equal(2, result.OutputCommands.Count(command => command.Command == "M4 S500"));
            Assert.Equal(2, result.OutputCommands.Count(command => command.Command == "F1234"));
        }

        [Fact]
        public void VisibilityAndOutput_AreIndependent()
        {
            LaserLayer layer = RectangleLayer();
            layer.OutputEnabled = false;
            layer.Visible = true;

            LayeredCompilation result = LayeredJobCompiler.Compile(DocumentWith(layer), 255, "M4", "M5");

            Assert.Empty(result.OutputCommands);
            Assert.NotEmpty(result.PreviewCommands);
            Assert.All(result.PreviewCommands, command => Assert.True(command.PreviewMuted));
        }

        [Theory]
        [InlineData(LayerMode.Fill)]
        [InlineData(LayerMode.OffsetFill)]
        public void FillModes_ProduceGeometry(LayerMode mode)
        {
            LaserLayer layer = RectangleLayer();
            layer.Mode = mode;
            layer.FillSettings.IntervalMm = 1;

            LayeredCompilation result = LayeredJobCompiler.Compile(DocumentWith(layer), 1000, "M4", "M5");

            Assert.False(result.HasErrors);
            Assert.Contains(result.OutputCommands, command => command.Command.StartsWith("G1 ", StringComparison.Ordinal));
        }

        [Fact]
        public void EmptyEnabledLayer_BlocksCompilation()
        {
            LaserLayer layer = new LaserLayer { ContentKind = LayerContentKind.Vector, Mode = LayerMode.Line };

            LayeredCompilation result = LayeredJobCompiler.Compile(DocumentWith(layer), 1000, "M4", "M5");

            Assert.True(result.HasErrors);
            Assert.True(layer.HasError);
        }

        [Fact]
        public void DuplicateColors_IndependentIds_CompileBothLayers()
        {
            LaserLayer first = RectangleLayer();
            LaserLayer second = RectangleLayer();
            LaserDocument document = DocumentWith(first);
            document.Layers.Add(second);

            LayeredCompilation result = LayeredJobCompiler.Compile(document, 1000, "M4", "M5");

            Assert.False(result.HasErrors);
            Assert.False(second.HasError);
            Assert.Contains(result.OutputCommands, command => command.SourceLayerId == first.Id);
            Assert.Contains(result.OutputCommands, command => command.SourceLayerId == second.Id);
        }

        [Fact]
        public void ProjectV2_RoundTripPreservesLayerAndImage()
        {
            string filename = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".lps");
            try
            {
                LaserLayer vector = RectangleLayer();
                LaserLayer raster = new LaserLayer
                {
                    Name = "Image",
                    Color = Color.Cyan,
                    ContentKind = LayerContentKind.Raster,
                    Mode = LayerMode.Image,
                    RasterItem = new RasterItem { ImageData = new byte[] { 1, 2, 3 }, BaseCommands = { "G0 X0", "M4 S10" } }
                };
                LaserDocument original = new LaserDocument();
                original.Layers.Add(vector);
                original.Layers.Add(raster);

                LayerProjectSerializer.Save(filename, original);
                LaserDocument loaded = LayerProjectSerializer.Load(filename);

                Assert.True(LayerProjectSerializer.IsVersion2(filename));
                Assert.Equal(2, loaded.Layers.Count);
                Assert.Equal(Color.Cyan.ToArgb(), loaded.Layers[1].ColorArgb);
                Assert.Equal(new byte[] { 1, 2, 3 }, loaded.Layers[1].RasterItem.ImageData);
                Assert.Equal("G0 X0", loaded.Layers[1].RasterItem.BaseCommands[0]);
            }
            finally
            {
                if (File.Exists(filename)) File.Delete(filename);
            }
        }

        [Fact]
        public void Resize_VectorChangesBoundsAndCompiledCoordinates()
        {
            LaserLayer layer = RectangleLayer();

            Assert.True(LayerGeometry.Resize(layer, 25, 15));
            RectangleF bounds;
            Assert.True(LayerGeometry.TryGetBounds(layer, out bounds));
            LayeredCompilation result = LayeredJobCompiler.Compile(DocumentWith(layer), 1000, "M4", "M5");

            Assert.Equal(25, bounds.Width, 3);
            Assert.Equal(15, bounds.Height, 3);
            Assert.Contains(result.OutputCommands, command => command.Command == "G1 X25 Y15");
        }

        [Fact]
        public void Bounds_MultipleLayersReturnsCombinedSelection()
        {
            LaserLayer first = RectangleLayer();
            LaserLayer second = RectangleLayer();
            second.TranslateX = 20;
            second.TranslateY = 5;

            RectangleF bounds;
            Assert.True(LayerGeometry.TryGetBounds(new[] { first, second }, out bounds));

            Assert.Equal(30, bounds.Width, 3);
            Assert.Equal(15, bounds.Height, 3);
        }

        [Fact]
        public void Resize_RasterTransformsAbsoluteAndRelativeCoordinates()
        {
            LaserLayer layer = new LaserLayer
            {
                ContentKind = LayerContentKind.Raster,
                Mode = LayerMode.Image,
                RasterItem = new RasterItem { BaseCommands = { "G90", "G0 X0 Y0", "G1 X10 Y20", "G91", "G1 X5 Y2" } }
            };

            Assert.True(LayerGeometry.Resize(layer, 30, 44));
            string[] commands = LayerGeometry.TransformRasterCommands(layer.RasterItem.BaseCommands, layer).ToArray();

            Assert.Equal("G1 X20 Y40", commands[2]);
            Assert.Equal("G1 X10 Y4", commands[4]);
        }

        [Fact]
        public void ProjectV2_RoundTripPreservesResizeTransform()
        {
            string filename = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".lps");
            try
            {
                LaserLayer layer = RectangleLayer();
                LayerGeometry.Resize(layer, 30, 20);
                LayerProjectSerializer.Save(filename, DocumentWith(layer));

                LaserLayer loaded = LayerProjectSerializer.Load(filename).Layers[0];

                Assert.Equal(3, loaded.ScaleX, 6);
                Assert.Equal(2, loaded.ScaleY, 6);
            }
            finally
            {
                if (File.Exists(filename)) File.Delete(filename);
            }
        }

        private static LaserDocument DocumentWith(LaserLayer layer)
        {
            LaserDocument document = new LaserDocument();
            document.Layers.Add(layer);
            return document;
        }

        private static LaserLayer RectangleLayer()
        {
            VectorPath path = new VectorPath { Closed = true };
            path.Points.Add(new LaserPoint(0, 0));
            path.Points.Add(new LaserPoint(10, 0));
            path.Points.Add(new LaserPoint(10, 10));
            path.Points.Add(new LaserPoint(0, 10));
            VectorItem item = new VectorItem();
            item.Paths.Add(path);
            LaserLayer layer = new LaserLayer { Name = "Red", Color = Color.Red, ContentKind = LayerContentKind.Vector, Mode = LayerMode.Line };
            layer.VectorItems.Add(item);
            return layer;
        }
    }
}
