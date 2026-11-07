using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using LaserGRBL.Layering;
using Xunit;

namespace LaserGRBL.Tests.Layering
{
    [Collection("Job execution")]
    public class CanvasEditingTests
    {
        private static LaserDocument Sample()
        {
            return new LaserDocument { Layers = { new LaserLayer { VectorItems = { CanvasObjects.Shape(CanvasShape.Rectangle, PointF.Empty, new PointF(20, 10)) } } } };
        }
        private static void Controller(Action<LayerDocumentController> action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var sync = new Control())
                    {
                        var core = new GrblCore(sync, null, null); var controller = core.LayerController;
                        controller.NewDocument();
                        try { action(controller); } finally { controller.ExitLayeredMode(); }
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        [Theory]
        [InlineData("Speed")][InlineData("Power")][InlineData("Passes")]
        public void Keyboard_NumericCellBeforeAndDuringEditing_OwnsKeysInsteadOfGlobalJog(string column)
        {
            Controller(c =>
            {
                using (var host = new Form())
                using (var manager = new LayerManagerControl())
                {
                    var grid = (DataGridView)typeof(LayerManagerControl).GetField("grid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(manager);
                    host.Controls.Add(manager); manager.CreateControl(); grid.Rows.Add(); grid.Enabled = true;
                    host.Show(); grid.CurrentCell = grid.Rows[0].Cells[column]; grid.Focus();
                    Assert.True(grid.ContainsFocus); Assert.False(grid.IsCurrentCellInEditMode);
                    Assert.True(CanvasKeyboard.IsTextEditing(host));
                    Assert.True(grid.BeginEdit(false)); Assert.True(CanvasKeyboard.IsTextEditing(host));
                    var editor = (TextBox)grid.EditingControl; editor.Text = "42";
                    Assert.True(grid.EndEdit()); Assert.Equal("42", Convert.ToString(grid.Rows[0].Cells[column].Value));
                }
            });
        }

        [Fact]
        public void ColorPicker_PaletteAndEscape_RestoreColorWithoutClosingDialog()
        {
            Controller(c =>
            {
                var type = typeof(LaserDocument).Assembly.GetType("LaserGRBL.Layering.LayerColorPickerForm");
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                using (var form = (Form)Activator.CreateInstance(type, flags, null, new object[] { Color.Red }, null))
                {
                    form.Show();
                    form.Controls.OfType<Button>().Single(b => b.Name == "LayerColorPreview" && b.BackColor.ToArgb() == Color.Blue.ToArgb()).PerformClick();
                    Assert.Equal(Color.Blue.ToArgb(), ((Color)type.GetProperty("SelectedColor").GetValue(form)).ToArgb());
                    type.GetField("colorBeforeEyedropper", flags).SetValue(form, Color.Red);
                    type.GetField("eyedropperActive", flags).SetValue(form, true);
                    object[] args = { new Message(), Keys.Escape };
                    Assert.True((bool)type.GetMethod("ProcessCmdKey", flags).Invoke(form, args));
                    Assert.Equal(Color.Red.ToArgb(), ((Color)type.GetProperty("SelectedColor").GetValue(form)).ToArgb()); Assert.Equal(DialogResult.None, form.DialogResult);
                }
            });
        }
        [Theory]
        [InlineData("move")][InlineData("resize")][InlineData("speed")][InlineData("power")][InlineData("passes")]
        [InlineData("visible")][InlineData("output")][InlineData("name")][InlineData("mode")][InlineData("fill")]
        public void History_CommittedMutation_UndoRedoRestoresExactDocument(string kind)
        {
            var doc = Sample(); var history = new CanvasHistory(); history.Reset(doc); var before = CanvasHistory.Key(doc); var layer = doc.Layers[0];
            switch (kind)
            {
                case "move": layer.TranslateX = 8; break; case "resize": layer.ScaleY = 2; break;
                case "speed": layer.SpeedMmPerMinute = 540; break; case "power": layer.PowerPercent = 14; break;
                case "passes": layer.Passes = 3; break; case "visible": layer.Visible = false; break;
                case "output": layer.OutputEnabled = false; break; case "name": layer.Name = "Nuevo"; break;
                case "mode": layer.Mode = LayerMode.Fill; break; case "fill": layer.FillSettings.AngleDegrees = 90; break;
            }
            history.Commit(doc, new CanvasSelectionState()); var after = CanvasHistory.Key(doc);
            CanvasSelectionState selection;
            Assert.Equal(before, CanvasHistory.Key(history.Restore(false, out selection)));
            Assert.Equal(after, CanvasHistory.Key(history.Restore(true, out selection)));
        }
        [Fact]
        public void History_DragSaveCancelAndNewBranch_PreserveBoundariesAndSavedState()
        {
            var doc = Sample(); var history = new CanvasHistory(); history.Reset(doc); var layer = doc.Layers[0];
            var selection = new CanvasSelectionState { Objects = true, Paths = { layer.VectorItems[0].Paths[0].Id } };
            history.Select(selection);
            for (int i = 0; i < 80; i++) layer.TranslateX = i;
            history.Commit(doc, selection); history.MarkSaved();
            layer.TranslateX = 900; Assert.Equal(79, history.Cancel().Layers[0].TranslateX); Assert.False(history.Cancel().IsDirty);
            CanvasSelectionState restoredSelection; var previous = history.Restore(false, out restoredSelection);
            Assert.Equal(selection.Paths, restoredSelection.Paths); Assert.True(previous.IsDirty); Assert.False(history.CanUndo);
            var saved = history.Restore(true, out restoredSelection); Assert.False(saved.IsDirty);
            previous = history.Restore(false, out restoredSelection); previous.Layers[0].Passes = 7; history.Commit(previous, selection);
            Assert.False(history.CanRedo);
        }
        [Fact]
        public void History_NoOpAndLimit_KeepOnly100RealOperations()
        {
            var doc = Sample(); var history = new CanvasHistory(); history.Reset(doc); history.Commit(doc, new CanvasSelectionState()); Assert.False(history.CanUndo);
            for (int i = 1; i <= 110; i++) { doc.Layers[0].TranslateX = i; history.Commit(doc, new CanvasSelectionState()); }
            CanvasSelectionState state; int count = 0; LaserDocument oldest = null;
            while (history.CanUndo) { oldest = history.Restore(false, out state); count++; }
            Assert.Equal(100, count); Assert.Equal(10, oldest.Layers[0].TranslateX);
        }
        [Fact]
        public void ColorMerge_TransformedSource_UsesDestinationSettingsAndRetainsWorldGeometry()
        {
            Controller(c =>
            {
                var source = c.InsertObject(CanvasObjects.Shape(CanvasShape.Rectangle, PointF.Empty, new PointF(10, 5)), null);
                source.ScaleX = 2; source.TranslateY = 40;
                var target = c.InsertObject(CanvasObjects.Shape(CanvasShape.Line, new PointF(70, 3), new PointF(80, 3)), null);
                target.PowerPercent = 22; target.SpeedMmPerMinute = 777; target.Passes = 4; target.ScaleY = 3; target.TranslateX = -10;
                c.Changed(false); var before = LayerGeometry.Transform(source.VectorItems[0].Paths[0], source);
                c.SetColor(source, target.Color);
                Assert.Same(target, Assert.Single(c.Document.Layers)); Assert.Equal(22, target.PowerPercent); Assert.Equal(777, target.SpeedMmPerMinute); Assert.Equal(4, target.Passes);
                var after = LayerGeometry.Transform(target.VectorItems[1].Paths[0], target);
                Assert.Equal(before.Points.Select(p => Math.Round(p.X,6)), after.Points.Select(p => Math.Round(p.X,6))); Assert.Equal(before.Points.Select(p => Math.Round(p.Y,6)), after.Points.Select(p => Math.Round(p.Y,6)));
                c.Undo(); Assert.Equal(2, c.Document.Layers.Count); c.Redo(); Assert.Single(c.Document.Layers);
                var compilation = LayeredJobCompiler.Compile(c.Document, 1000, "M4", "M5");
                Assert.Contains(compilation.OutputCommands, cmd => cmd.Command == "F777"); Assert.Contains(compilation.OutputCommands, cmd => cmd.Command == "M4 S220");
            });
        }
        [Fact]
        public void Group_NestedThenUngroup_OnlyRemovesOuterLevel()
        {
            Controller(c =>
            {
                var a = c.InsertObject(CanvasObjects.Shape(CanvasShape.Rectangle, PointF.Empty, new PointF(10, 5)), null);
                var b = c.InsertObject(CanvasObjects.Shape(CanvasShape.Line, new PointF(20, 0), new PointF(25, 5)), null);
                var inner = c.Group(new[] { a, b }, new VectorSelection[0], false);
                var raster = new LaserLayer { ContentKind = LayerContentKind.Raster, Mode = LayerMode.Image, RasterItem = new RasterItem() }; c.Document.Layers.Add(raster); c.Changed(false);
                var outer = c.Group(new[] { a, b, raster }, new VectorSelection[0], false);
                Assert.Equal(outer, CanvasGroups.Root(c.Document, a.GroupId)); Assert.Equal(inner, a.GroupId);
                c.Ungroup(new[] { a }, new VectorSelection[0], false);
                Assert.Equal(inner, a.GroupId); Assert.Equal(Guid.Empty, raster.GroupId); Assert.Single(c.Document.Groups);
                c.Undo(); Assert.Equal(2, c.Document.Groups.Count);
            });
        }
        [Fact]
        public void SvgImport_NestedMulticolorGroups_KeepAncestryAndCopyGetsNewGroups()
        {
            string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".svg");
            try
            {
                // File parsing is also checked by the .NET Framework integration harness;
                // its legacy System.Web handler cannot load in this .NET Core test host.
                var svg = new Svg.SvgDocument { Width = 100, Height = 100 };
                var outer = new Svg.SvgGroup { ID = "outer" }; var inner = new Svg.SvgGroup { ID = "inner" };
                outer.Transforms = new Svg.Transforms.SvgTransformCollection { new Svg.Transforms.SvgTranslate(5, 4) };
                svg.Children.Add(outer); outer.Children.Add(inner);
                inner.Children.Add(new Svg.SvgRectangle { Width = 10, Height = 12, Fill = new Svg.SvgColourServer(Color.Red) });
                outer.Children.Add(new Svg.SvgCircle { CenterX = 30, CenterY = 30, Radius = 5, Fill = new Svg.SvgColourServer(Color.Blue) });
                var doc = new LaserDocument(); SvgLayerImporter.Import(svg, file, doc, 1000, 50);
                Assert.Equal(2, doc.Groups.Count); Assert.Equal(2, doc.Layers.Count);
                var layer = doc.Layers[0]; var selected = new VectorSelection(layer, layer.VectorItems[0], layer.VectorItems[0].Paths[0]);
                var expanded = CanvasGroups.Expand(doc, new[] { selected }); Assert.Equal(2, expanded.Count);
                var originalIds = doc.Groups.Select(g => g.Id).ToList(); var clipboard = new CanvasClipboard(); clipboard.CopyObjects(doc, expanded); clipboard.Paste(doc, false);
                Assert.Equal(4, doc.Groups.Count); Assert.DoesNotContain(doc.Groups[2].Id, originalIds);
                var newPaths = doc.Layers.SelectMany(l => l.VectorItems).SelectMany(i => i.Paths).Where(p => clipboard.LastPastedPaths.Contains(p.Id)).ToList();
                Assert.Equal(2, newPaths.Count); Assert.NotEqual(CanvasGroups.Root(doc, selected.Path.GroupId), CanvasGroups.Root(doc, newPaths[0].GroupId));
            }
            finally { File.Delete(file); }
        }
        [Theory]
        [InlineData(CanvasShape.Rectangle, true)][InlineData(CanvasShape.Ellipse, true)][InlineData(CanvasShape.Line, false)]
        public void Shape_ReversedGesture_ProducesExpectedBoundsAndClosure(CanvasShape shape, bool closed)
        {
            var item = CanvasObjects.Shape(shape, new PointF(20, 10), PointF.Empty);
            var path = Assert.Single(item.Paths); Assert.Equal(closed, path.Closed); var bounds = CanvasObjects.Bounds(item.Paths);
            Assert.Equal(20, bounds.Width, 2); Assert.Equal(10, bounds.Height, 2);
        }
        [Fact]
        public void Text_AccentsMultilineAndHoles_EditKeepsScaleAndStoresMetadata()
        {
            var text = new TextObject { Content = "Oñá\nMéxico", SizeMm = 10 };
            var item = CanvasObjects.Text(text, new PointF(15, 20)); Assert.True(item.Paths.Count > 8); Assert.All(item.Paths, p => Assert.True(p.Closed));
            foreach (var p in item.Paths.SelectMany(p => p.Points)) { p.X = 15 + (p.X - 15) * 2; p.Y = 20 + (p.Y - 20) * 3; }
            var old = CanvasObjects.Bounds(item.Paths); text.Content = "Oñá\nMéxico largo";
            var edited = CanvasObjects.Text(text, new PointF(old.Left, old.Top), item); var bounds = CanvasObjects.Bounds(edited.Paths);
            Assert.Equal(item.Id, edited.Id); Assert.Equal(old.Left, bounds.Left, 3); Assert.Equal(item.Paths[0].Points[0].Y, edited.Paths[0].Points[0].Y, 3);
            Assert.Equal(2, bounds.Width / edited.Text.ReferenceWidth, 3); Assert.Equal(3, bounds.Height / edited.Text.ReferenceHeight, 3);
            Assert.Throws<ArgumentException>(() => CanvasObjects.Text(new TextObject { Content = "x", FontFamily = "__missing_font__" }, PointF.Empty));
        }
        [Fact]
        public void Project_V3RoundTripAndV2Migration_PreserveIdsTextAndGroups()
        {
            string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lps");
            try
            {
                var doc = Sample(); var group = new CanvasGroup { Name = "Grupo" }; doc.Groups.Add(group);
                var text = CanvasObjects.Text(new TextObject { Content = "O" }, PointF.Empty); doc.Layers[0].VectorItems.Add(text); text.Paths.ForEach(p => p.GroupId = group.Id);
                // Missing font must not cause regeneration or substitution on load.
                text.Text.FontFamily = "__missing_font__";
                LayerProjectSerializer.Save(file, doc); var loaded = LayerProjectSerializer.Load(file);
                Assert.Equal(4, loaded.Version); Assert.Equal(group.Id, loaded.Groups[0].Id);
                Assert.Equal(text.Id, loaded.Layers[0].VectorItems[1].Id); Assert.Equal("__missing_font__", loaded.Layers[0].VectorItems[1].Text.FontFamily);
                Assert.Equal(text.Paths[0].Id, loaded.Layers[0].VectorItems[1].Paths[0].Id);
                var dto = LayerProjectSerializer.LaserProjectDto.FromModel(Sample()); dto.Version = 2;
                dto.Layers[0].VectorItems[0].Id = Guid.Empty; dto.Layers[0].VectorItems[0].Paths[0].Id = Guid.Empty;
                using (var stream = File.Create(file)) using (var zip = new GZipStream(stream, CompressionMode.Compress)) new XmlSerializer(dto.GetType()).Serialize(zip, dto);
                loaded = LayerProjectSerializer.Load(file); Assert.Equal(4, loaded.Version);
                Assert.NotEqual(Guid.Empty, loaded.Layers[0].VectorItems[0].Id); Assert.NotEqual(Guid.Empty, loaded.Layers[0].VectorItems[0].Paths[0].Id);
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void Controller_CutUndoAndPaste_RestoreSelectionAndKeepClipboardSnapshot()
        {
            Controller(c =>
            {
                var layer = c.InsertObject(CanvasObjects.Shape(CanvasShape.Rectangle, new PointF(12, 7), new PointF(22, 17)), null);
                var path = layer.VectorItems[0].Paths[0];
                c.SetSelection(new CanvasSelectionState { Objects = true, Layers = { layer.Id }, Paths = { path.Id } });
                var selection = new VectorSelection(layer, layer.VectorItems[0], path);
                c.CutObjects(new[] { selection }); Assert.False(layer.HasContent); Assert.True(c.Clipboard.HasContent);
                CanvasSelectionState restored = null; c.SelectionRestored += state => restored = state;
                c.Undo(); Assert.True(c.Document.Layers[0].HasContent); Assert.Equal(path.Id, Assert.Single(restored.Paths));
                c.Redo(); Assert.False(c.Document.Layers[0].HasContent);
                c.Paste(); Assert.Equal(12, c.Document.Layers[0].VectorItems[0].Paths[0].Points[0].X);
                Assert.True(c.CanUndo); c.Undo(); Assert.False(c.Document.Layers[0].HasContent);
                c.NewDocument(); Assert.False(c.CanUndo); Assert.False(c.CanRedo); Assert.False(c.Clipboard.HasContent);
            });
        }

        [Fact]
        public void Controller_MixedGroup_CopiesAndCutsRasterTogetherWithVector()
        {
            Controller(c =>
            {
                var layer = c.InsertObject(CanvasObjects.Shape(CanvasShape.Rectangle, PointF.Empty, new PointF(10, 5)), null);
                var raster = new LaserLayer { Color = layer.Color, ContentKind = LayerContentKind.Raster, Mode = LayerMode.Image,
                    RasterItem = new RasterItem { ImageData = new byte[] { 1 }, BaseCommands = { "G0 X20 Y20", "M4 S100", "G1 X25 Y25", "M5" } } };
                c.Document.Layers.Add(raster); c.Changed(false);
                Assert.Equal(2, c.Document.Layers.Count);
                c.Group(c.Document.Layers, new VectorSelection[0], false);
                var selected = new VectorSelection(layer, layer.VectorItems[0], layer.VectorItems[0].Paths[0]);
                c.CopyObjects(new[] { selected }); var pasted = c.Paste();
                Assert.Equal(2, pasted.Count); Assert.Equal(2, c.Document.Layers.Count(l => l.ContentKind == LayerContentKind.Raster));
                var newRaster = c.Document.Layers.Last(); Assert.NotEqual(raster.GroupId, newRaster.GroupId); Assert.False(newRaster.OutputEnabled);
                c.CutObjects(new[] { selected }); Assert.DoesNotContain(raster, c.Document.Layers); Assert.Contains(newRaster, c.Document.Layers);
                Assert.Single(layer.VectorItems); // Only the pasted group remains.
                c.Undo(); Assert.Equal(2, c.Document.Layers.Count(l => l.ContentKind == LayerContentKind.Raster));
            });
        }

        [Fact]
        public void Controller_TextInTransformedLayer_EditAndConversionAreUndoable()
        {
            Controller(c =>
            {
                var layer = c.InsertObject(CanvasObjects.Shape(CanvasShape.Line, PointF.Empty, new PointF(4, 0)), null);
                layer.ScaleX = 2; layer.ScaleY = 3; layer.TranslateX = 11; layer.TranslateY = 17; c.Changed(false);
                var text = new TextObject { Content = "O", SizeMm = 10 };
                var item = CanvasObjects.Text(text, new PointF(30, 40)); c.InsertObject(item, layer);
                var initial = CanvasObjects.Bounds(item.Paths.Select(p => LayerGeometry.Transform(p, layer)));
                text.Content = "OO"; c.UpdateText(layer, item, text); item = layer.VectorItems[1];
                var edited = CanvasObjects.Bounds(item.Paths.Select(p => LayerGeometry.Transform(p, layer)));
                Assert.Equal(initial.Left, edited.Left, 3); Assert.Equal(initial.Top, edited.Top, 3); Assert.Equal(initial.Height, edited.Height, 3);
                Assert.True(edited.Width > initial.Width * 1.5);
                c.ConvertText(new[] { new VectorSelection(layer, item, item.Paths[0]) }); Assert.Null(item.Text);
                c.Undo(); Assert.NotNull(c.Document.Layers[0].VectorItems[1].Text);
            });
        }

        [Fact]
        public void Controller_AppendImport_IsOneUndoableChangeAndRetainsDestinationSettings()
        {
            Controller(c =>
            {
                var first = c.InsertObject(CanvasObjects.Shape(CanvasShape.Line, PointF.Empty, new PointF(10, 10)), null);
                first.PowerPercent = 15; c.Changed(false);
                var svg = new Svg.SvgDocument { Width = 100, Height = 100 };
                svg.Children.Add(new Svg.SvgRectangle { Width = 10, Height = 10, Fill = new Svg.SvgColourServer(first.Color) });
                SvgLayerImporter.Import(svg, "sample.svg", c.Document, 500, 90); c.Changed(false);
                Assert.Single(c.Document.Layers); Assert.Equal(2, first.VectorItems.Count); Assert.Equal(15, first.PowerPercent);
                c.Undo(); Assert.Single(c.Document.Layers[0].VectorItems); c.Redo(); Assert.Equal(2, c.Document.Layers[0].VectorItems.Count);
            });
        }

        [Fact]
        public void Controller_SaveDuringQueuedCellCommit_TracksSavedRevisionCorrectly()
        {
            Controller(c =>
            {
                string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lps");
                try
                {
                    var layer = c.InsertObject(CanvasObjects.Shape(CanvasShape.Line, PointF.Empty, new PointF(10, 10)), null);
                    layer.SpeedMmPerMinute = 2345; // Cell model updated; Changed is still queued.
                    c.Save(file); c.Changed(false);
                    Assert.False(c.Document.IsDirty); Assert.Equal(2345, LayerProjectSerializer.Load(file).Layers[0].SpeedMmPerMinute);
                    c.Undo(); Assert.True(c.Document.IsDirty); Assert.Equal(1000, c.Document.Layers[0].SpeedMmPerMinute);
                    c.Redo(); Assert.False(c.Document.IsDirty);
                }
                finally { File.Delete(file); }
            });
        }

        [Fact]
        public void LayerGrid_ImmediateUndoBeforeQueuedRefresh_RevertsConfirmedCell()
        {
            Controller(c =>
            {
                var layer = c.InsertObject(CanvasObjects.Shape(CanvasShape.Line, PointF.Empty, new PointF(10, 10)), null);
                c.History.Reset(c.Document); layer.SpeedMmPerMinute = 2345;
                int changes = 0; c.HistoryStateChanged += () => changes++;
                c.RecordCellEdit(); Assert.True(c.CanUndo); Assert.False(c.IsCompilationCurrent); Assert.Equal(1, changes);
                c.Undo(); Assert.Equal(1000, c.Document.Layers[0].SpeedMmPerMinute);
                c.Changed(false); // Deferred cell refresh runs after Undo.
                Assert.True(c.CanRedo); c.Redo(); Assert.Equal(2345, c.Document.Layers[0].SpeedMmPerMinute);
            });
        }

        [Fact]
        public void OpenGl_FileReloadWithoutAutoFit_WakesRendererOnEveryUndoRedo()
        {
            Controller(c =>
            {
                c.InsertObject(CanvasObjects.Shape(CanvasShape.Rectangle, PointF.Empty, new PointF(10,10)), null);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var core = typeof(LayerDocumentController).GetField("core",flags).GetValue(c);
                // Exercise the real notification handler without starting a GPU context.
                var panel = (LaserGRBL.UserControls.GrblPanel3D)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(LaserGRBL.UserControls.GrblPanel3D));
                GC.SuppressFinalize(panel);
                typeof(LaserGRBL.UserControls.GrblPanel3D).GetField("Core",flags).SetValue(panel,core);
                typeof(LaserGRBL.UserControls.GrblPanel3D).GetField("fitNextFile",flags).SetValue(panel,false);
                using (var redraw = new AutoResetEvent(false))
                {
                    panel.RR = redraw;
                    for (int i=0;i<3;i++)
                    {
                        typeof(LaserGRBL.UserControls.GrblPanel3D).GetField("mReload",flags).SetValue(panel,false);
                        typeof(LaserGRBL.UserControls.GrblPanel3D).GetMethod("OnFileLoaded",flags).Invoke(panel,new object[] { 0L,"Layered project" });
                        Assert.True(redraw.WaitOne(0));
                        Assert.True((bool)typeof(LaserGRBL.UserControls.GrblPanel3D).GetField("mReload",flags).GetValue(panel));
                        Assert.False((bool)typeof(LaserGRBL.UserControls.GrblPanel3D).GetField("fitNextFile",flags).GetValue(panel));
                    }
                }
            });
        }

    }
}
