using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using LaserGRBL.Layering;
using Xunit;

namespace LaserGRBL.Tests.Layering
{
    public class CanvasLayoutTests
    {
        private static LaserLayer Shape(Color color, CanvasShape shape, PointF start, PointF end)
        { return new LaserLayer { Color = color, VectorItems = { CanvasObjects.Shape(shape, start, end) } }; }
        private static LaserDocument Sample()
        {
            var doc = new LaserDocument { Layers = {
                Shape(Color.Red, CanvasShape.Rectangle, new PointF(10, 20), new PointF(30, 30)),
                Shape(Color.Blue, CanvasShape.Rectangle, new PointF(70, 80), new PointF(80, 100)) } };
            doc.NormalizeOrder(); return doc;
        }
        [Theory]
        [InlineData(CanvasAlignmentAction.Left, -15, 0)]
        [InlineData(CanvasAlignmentAction.HorizontalCenter, 50, 0)]
        [InlineData(CanvasAlignmentAction.Right, 115, 0)]
        [InlineData(CanvasAlignmentAction.Top, 0, -10)]
        [InlineData(CanvasAlignmentAction.VerticalCenter, 0, -20)]
        [InlineData(CanvasAlignmentAction.Bottom, 0, -30)]
        [InlineData(CanvasAlignmentAction.Center, 50, -20)]
        public void Align_TableWithWorkOffset_MovesEntireSelectionAsBlock(CanvasAlignmentAction action, double dx, double dy)
        {
            var doc = Sample(); var before = CanvasLayout.Units(doc, false).Select(u => u.Bounds).ToArray();
            var state = new CanvasSelectionState { Layers = doc.Layers.Select(l => l.Id).ToList() };
            Assert.True(CanvasLayout.Align(doc, state, action, new RectangleF(-5, -10, 200, 100)));
            var after = CanvasLayout.Units(doc, false).Select(u => u.Bounds).ToArray();
            for (int i = 0; i < 2; i++) { Assert.Equal(before[i].X + dx, after[i].X, 5); Assert.Equal(before[i].Y + dy, after[i].Y, 5); Assert.Equal(before[i].Size, after[i].Size); }
        }
        [Theory]
        [InlineData(CanvasAlignmentAction.Left, 70, 20)]
        [InlineData(CanvasAlignmentAction.HorizontalCenter, 65, 20)]
        [InlineData(CanvasAlignmentAction.Right, 60, 20)]
        [InlineData(CanvasAlignmentAction.Top, 10, 90)]
        [InlineData(CanvasAlignmentAction.VerticalCenter, 10, 85)]
        [InlineData(CanvasAlignmentAction.Bottom, 10, 80)]
        [InlineData(CanvasAlignmentAction.Center, 65, 85)]
        public void Align_LastSelectedReference_StaysFixed(CanvasAlignmentAction action, double x, double y)
        {
            var doc = Sample(); var reference = doc.Layers[1];
            var state = new CanvasSelectionState { Layers = doc.Layers.Select(l => l.Id).ToList(), ReferenceId = reference.Id };
            var original = CanvasLayout.Units(doc, false)[1].Bounds;
            Assert.True(CanvasLayout.Align(doc, state, action, null));
            var units = CanvasLayout.Units(doc, false);
            Assert.Equal(x, units[0].Bounds.Left, 5); Assert.Equal(y, units[0].Bounds.Top, 5); Assert.Equal(original, units[1].Bounds);
            Assert.False(CanvasLayout.Align(doc, state, action, null));
        }
        [Fact]
        public void Align_ObjectsInsideScaledLayer_CompensatesLocalCoordinatesAndKeepsSettings()
        {
            var doc = Sample(); var source = doc.Layers[0]; source.ScaleX = 2; source.ScaleY = 3; source.TranslateX = 7;
            source.SpeedMmPerMinute = 1234; source.PowerPercent = 42; source.Passes = 4;
            var units = CanvasLayout.Units(doc, true); var state = new CanvasSelectionState { Objects = true, Paths = units.SelectMany(u => u.Objects).Select(o => o.Path.Id).ToList(), ReferenceId = units[1].Id };
            var size = units[0].Bounds.Size;
            Assert.True(CanvasLayout.Align(doc, state, CanvasAlignmentAction.Center, null));
            units = CanvasLayout.Units(doc, true);
            Assert.Equal(size, units[0].Bounds.Size);
            Assert.Equal(units[1].Bounds.Left + units[1].Bounds.Width/2, units[0].Bounds.Left + units[0].Bounds.Width/2, 5);
            Assert.Equal(units[1].Bounds.Top + units[1].Bounds.Height/2, units[0].Bounds.Top + units[0].Bounds.Height/2, 5);
            Assert.Equal(2, source.ScaleX); Assert.Equal(7, source.TranslateX); Assert.Equal(1234, source.SpeedMmPerMinute); Assert.Equal(42, source.PowerPercent); Assert.Equal(4, source.Passes);
        }
        [Theory]
        [InlineData(0, 0, 20, 20, false)][InlineData(20, 0, 0, 20, false)]
        [InlineData(0, 20, 20, 0, true)][InlineData(20, 20, 0, 0, true)]
        [InlineData(20, 0, 0, 0, false)]
        public void Marquee_Direction_DependsOnVerticalMovement(float x1, float y1, float x2, float y2, bool crossing)
        { Assert.Equal(crossing, CanvasLayout.Crossing(new PointF(x1, y1), new PointF(x2, y2))); }
        [Fact]
        public void Marquee_Crossing_UsesContourIntersectionInsteadOfBoundingBox()
        {
            var doc = new LaserDocument { Layers = { Shape(Color.Red, CanvasShape.Ellipse, PointF.Empty, new PointF(10, 10)) } };
            var unit = CanvasLayout.Units(doc, true).Single();
            Assert.False(CanvasLayout.Matches(unit, new RectangleF(0, 0, 1, 1), true, p => p));
            Assert.False(CanvasLayout.Matches(unit, new RectangleF(4, 4, 1, 1), true, p => p));
            Assert.True(CanvasLayout.Matches(unit, new RectangleF(10, 4, 1, 2), true, p => p));
            Assert.False(CanvasLayout.Matches(unit, new RectangleF(0, 0, 5, 10), false, p => p));
            Assert.True(CanvasLayout.Matches(unit, new RectangleF(0, 0, 10, 10), false, p => p));
        }
        [Theory]
        [InlineData(0, 0, 10, 0)][InlineData(0, 0, 0, 10)]
        public void Marquee_AxisAlignedLines_AreSelectableAsObjectsAndLayers(float x1, float y1, float x2, float y2)
        {
            var doc = new LaserDocument { Layers = { Shape(Color.Red, CanvasShape.Line, new PointF(x1,y1), new PointF(x2,y2)) } };
            foreach (bool objects in new[] { false, true })
            {
                var unit = Assert.Single(CanvasLayout.Units(doc, objects));
                Assert.True(CanvasLayout.Matches(unit, new RectangleF(-1,-1,12,12), false, p => p));
                Assert.True(CanvasLayout.Matches(unit, new RectangleF(0,0,1,1), true, p => p));
            }
        }
        [Fact]
        public void Marquee_GroupsAndHiddenMembers_SelectAsOneUnit()
        {
            var doc = Sample(); var group = new CanvasGroup { Name = "Group" }; doc.Groups.Add(group);
            foreach (var layer in doc.Layers) { layer.GroupId = group.Id; layer.VectorItems[0].Paths[0].GroupId = group.Id; }
            var unit = Assert.Single(CanvasLayout.Units(doc, true));
            Assert.False(CanvasLayout.Matches(unit, new RectangleF(9,19,22,12), false, p => p));
            Assert.True(CanvasLayout.Matches(unit, new RectangleF(9,19,22,12), true, p => p));
            doc.Layers[1].Visible = false; unit = Assert.Single(CanvasLayout.Units(doc, true));
            Assert.True(CanvasLayout.Matches(unit, new RectangleF(9,19,22,12), false, p => p));
            unit.Move(5, 7); Assert.Equal(5, doc.Layers[1].VectorItems[0].Paths[0].Points[0].X - 70);
            doc.Layers[0].Visible = false; unit = Assert.Single(CanvasLayout.Units(doc, true));
            Assert.False(CanvasLayout.Matches(unit, new RectangleF(-100,-100,1000,1000), true, p => p));
        }
        [Fact]
        public void Marquee_FilledCompoundShape_HoleIsNotSelectedAsPaintedArea()
        {
            var layer = Shape(Color.Red, CanvasShape.Rectangle, PointF.Empty, new PointF(20,20)); layer.Mode = LayerMode.Fill;
            layer.VectorItems[0].Paths.Add(CanvasObjects.Shape(CanvasShape.Rectangle, new PointF(5,5), new PointF(15,15)).Paths[0]);
            var doc = new LaserDocument { Layers = { layer } }; var unit = Assert.Single(CanvasLayout.Units(doc, false));
            Assert.False(CanvasLayout.Matches(unit, new RectangleF(8,8,2,2), true, p => p));
            Assert.True(CanvasLayout.Matches(unit, new RectangleF(1,1,2,2), true, p => p));
        }
        [Fact]
        public void Units_TextAndImages_AreAtomicAndMoveTogetherWhenGrouped()
        {
            var doc = Sample(); doc.Layers.RemoveAt(1);
            var text = CanvasObjects.Text(new TextObject { Content = "O" }, new PointF(40,40)); doc.Layers[0].VectorItems.Add(text);
            var raster = new LaserLayer { ContentKind = LayerContentKind.Raster, Mode = LayerMode.Image, RasterItem = new RasterItem { BaseCommands = { "G0 X50 Y50", "M4 S20", "G1 X60 Y60", "M5" } } }; doc.Layers.Add(raster);
            var units = CanvasLayout.Units(doc, true); Assert.Equal(3, units.Count); Assert.True(units.Single(u => u.Id == text.Id).Objects.Count > 1);
            var group = new CanvasGroup(); doc.Groups.Add(group); raster.GroupId = group.Id; text.Paths.ForEach(p => p.GroupId = group.Id);
            var combined = CanvasLayout.Units(doc, true).Single(u => u.Id == group.Id);
            var before = combined.Bounds; combined.Move(10,20);
            var after = CanvasLayout.Units(doc, true).Single(u => u.Id == group.Id).Bounds;
            Assert.Equal(before.X+10, after.X, 4); Assert.Equal(before.Y+20, after.Y, 4); Assert.Equal(before.Size, after.Size);
        }
        [Fact]
        public void Viewport_PanAndRenderRevision_DoNotChangeDocumentOrLoseInverseMapping()
        {
            var doc = Sample(); var before = CanvasHistory.Key(doc); var viewport = new CanvasViewport();
            using (var original = new Matrix(2,0,0,-2,20,400))
            {
                Assert.True(viewport.Accept(original, viewport.Revision, true)); int oldRevision = viewport.Revision;
                viewport.Pan(30,-50, original); PointF client, machine;
                Assert.True(viewport.TryTransform(new PointF(10,20), false, out client)); Assert.Equal(new PointF(70,310), client);
                Assert.True(viewport.TryTransform(client, true, out machine)); Assert.Equal(new PointF(10,20), machine);
                Assert.False(viewport.Accept(original, oldRevision, true));
                Assert.Equal(before, CanvasHistory.Key(doc)); Assert.False(doc.IsDirty);
                viewport.Reset(); Assert.False(viewport.TryTransform(client, true, out machine));
            }
        }
        [Fact]
        public void Selection_ReferenceId_IsPreservedByHistory()
        {
            var doc = Sample(); var history = new CanvasHistory(); history.Reset(doc);
            var state = new CanvasSelectionState { Layers = doc.Layers.Select(l => l.Id).ToList(), ReferenceId = doc.Layers[1].Id };
            history.Select(state); CanvasLayout.Align(doc,state,CanvasAlignmentAction.Center,null); history.Commit(doc,state);
            CanvasSelectionState restored; history.Restore(false,out restored); Assert.Equal(state.ReferenceId, restored.ReferenceId);
        }
        [Theory]
        [InlineData(40,20,20,4)][InlineData(-40,-20,-20,-4)]
        public void Viewport_OpenGlPan_UsesBothViewportScales(int x, int y, double expectedX, double expectedY)
        {
            var delta = CanvasViewport.PanDelta(200,100,400,500,new Point(x,y));
            Assert.Equal(expectedX,delta.X,6); Assert.Equal(expectedY,delta.Y,6);
        }
    }
}
