using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.IO.Compression;
using System.Xml.Serialization;
using LaserGRBL.Layering;
using Xunit;

namespace LaserGRBL.Tests.Layering
{
    [Collection("Job execution")]
    public class AffineEditingTests
    {
        private static void Near(LaserPoint expected,LaserPoint actual) { Assert.Equal(expected.X,actual.X,6);Assert.Equal(expected.Y,actual.Y,6); }
        [Theory]
        [InlineData(0)][InlineData(45)][InlineData(90)][InlineData(-35)]
        public void Rotation_ArbitraryPivot_InverseRestoresPoints(double angle)
        {
            var m=AffineTransform.Rotation(angle,new PointF(7,12)).Then(AffineTransform.Shear(20,-10,PointF.Empty));
            var p=new LaserPoint(25,32);var q=m.Apply(p.X,p.Y);Near(p,m.Inverse().Apply(q.X,q.Y));Assert.True(m.Valid);
            var pivot=AffineTransform.Rotation(angle,new PointF(7,12)).Apply(7,12);Near(new LaserPoint(7,12),pivot);
        }
        [Fact]
        public void Transform_SingularOrNonfinite_IsRejected()
        {
            Assert.False(new AffineTransform{A=0}.Valid);Assert.False(new AffineTransform{X=double.NaN}.Valid);
            Assert.Throws<InvalidOperationException>(()=>new AffineTransform{D=0}.Inverse());
        }
        [Theory]
        [InlineData(LayerMode.Line)][InlineData(LayerMode.Fill)][InlineData(LayerMode.OffsetFill)]
        public void Compiler_CanceledRevision_StopsWithoutMarkingLayerAsError(LayerMode mode)
        {
            var layer=new LaserLayer{Mode=mode};layer.VectorItems.Add(CanvasObjects.Shape(CanvasShape.Rectangle,PointF.Empty,new PointF(50,30)));
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();Assert.Throws<OperationCanceledException>(()=>LayeredJobCompiler.Compile(new LaserDocument{Layers={layer}},1000,"M4","M5",1,cancellation.Token));
                Assert.False(layer.HasError);
            }
        }
        [Theory]
        [InlineData(2)][InlineData(3)]
        public void Project_LegacyVersion_MigratesScaleAndIdsWithoutMovingGeometry(int version)
        {
            var layer=new LaserLayer{ScaleX=2,ScaleY=3,TranslateX=11,TranslateY=17};layer.VectorItems.Add(CanvasObjects.Shape(CanvasShape.Rectangle,PointF.Empty,new PointF(10,7)));
            var doc=new LaserDocument{Layers={layer}};var dto=LayerProjectSerializer.LaserProjectDto.FromModel(doc);dto.Version=version;
            string file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".lps");
            try
            {
                using(var stream=File.Create(file))using(var zip=new GZipStream(stream,CompressionMode.Compress))new XmlSerializer(typeof(LayerProjectSerializer.LaserProjectDto)).Serialize(zip,dto);
                var loaded=LayerProjectSerializer.Load(file);Assert.Equal(4,loaded.Version);Assert.Equal(layer.Id,loaded.Layers[0].Id);
                var before=LayerGeometry.Transform(layer.VectorItems[0].Paths[0],layer);var after=LayerGeometry.Transform(loaded.Layers[0].VectorItems[0].Paths[0],loaded.Layers[0]);
                for(int i=0;i<before.Points.Count;i++)Near(before.Points[i],after.Points[i]);
            }
            finally{File.Delete(file);}
        }
        [Fact]
        public void MulticolorGroup_TransformsEachMemberOnceAndRetainsLayerSettings()
        {
            var g=new CanvasGroup();var a=new LaserLayer{GroupId=g.Id,Color=Color.Red,SpeedMmPerMinute=777,PowerPercent=22,Passes=3};
            var b=new LaserLayer{GroupId=g.Id,Color=Color.Blue,TranslateX=30};
            a.VectorItems.Add(CanvasObjects.Shape(CanvasShape.Rectangle,PointF.Empty,new PointF(10,20)));
            b.VectorItems.Add(CanvasObjects.Shape(CanvasShape.Line,PointF.Empty,new PointF(20,0)));
            var doc=new LaserDocument{Layers={a,b},Groups={g}};var selection=new CanvasSelectionState{Layers={a.Id,b.Id}};
            var m=AffineTransform.Rotation(90,new PointF(12,9));var original=LayerGeometry.Transform(b.VectorItems[0].Paths[0],b);
            AffineTransform.Selection(doc,selection,m);
            var actual=LayerGeometry.Transform(b.VectorItems[0].Paths[0],b);for(int i=0;i<actual.Points.Count;i++)Near(m.Apply(original.Points[i].X,original.Points[i].Y),actual.Points[i]);
            Assert.Equal(777,a.SpeedMmPerMinute);Assert.Equal(22,a.PowerPercent);Assert.Equal(3,a.Passes);
            var result=LayeredJobCompiler.Compile(doc,1000,"M4","M5");Assert.False(result.HasErrors);Assert.Contains(result.OutputCommands,c=>c.Command=="F777");Assert.Contains(result.OutputCommands,c=>c.Command=="M4 S220");
        }
        [Fact]
        public void ColorMerge_RotatedAndShearedLayers_RetainsWorldCoordinatesAndDestination()
        {
            var a=new LaserLayer{Color=Color.Red};var b=new LaserLayer{Color=Color.Red,PowerPercent=35};
            AffineTransform.Rotation(90,PointF.Empty).SetLayer(a);AffineTransform.Shear(20,10,new PointF(5,3)).SetLayer(b);
            a.VectorItems.Add(CanvasObjects.Shape(CanvasShape.Rectangle,new PointF(1,2),new PointF(11,7)));
            b.VectorItems.Add(CanvasObjects.Shape(CanvasShape.Ellipse,new PointF(20,20),new PointF(30,28)));
            var before=LayerGeometry.Transform(b.VectorItems[0].Paths[0],b);var doc=new LaserDocument{Layers={a,b}};
            CanvasGroups.MergeColors(doc);Assert.Same(a,Assert.Single(doc.Layers));var after=LayerGeometry.Transform(a.VectorItems[1].Paths[0],a);
            for(int i=0;i<before.Points.Count;i++)Near(before.Points[i],after.Points[i]);
            var clipboard=new CanvasClipboard();clipboard.CopyLayers(doc,new[]{a});clipboard.Paste(doc,false);
            var copy=LayerGeometry.Transform(a.VectorItems[2].Paths[0],a);var source=LayerGeometry.Transform(a.VectorItems[0].Paths[0],a);
            for(int i=0;i<source.Points.Count;i++)Near(new LaserPoint(source.Points[i].X+5,source.Points[i].Y+5),copy.Points[i]);
        }
        [Theory]
        [InlineData(45)][InlineData(90)][InlineData(-30)]
        public void Text_EditAfterRotationShearAndResize_PreservesGlyphPose(double angle)
        {
            var item=CanvasObjects.Text(new TextObject{Content="Oá\nMéxico",SizeMm=10},new PointF(10,20));
            var m=AffineTransform.Rotation(angle,new PointF(10,15)).Then(AffineTransform.Shear(20,-5,PointF.Empty)).Then(new AffineTransform{A=2,D=3});
            foreach(var path in item.Paths)AffineTransform.Path(path,m);
            var metadata=item.Text.Clone();metadata.Content+=" largo";
            var edited=CanvasObjects.Text(metadata,PointF.Empty,item);
            Assert.Equal(item.Id,edited.Id);Assert.Equal(metadata.Content,edited.Text.Content);
            for(int i=0;i<item.Paths[0].Points.Count;i++)Near(item.Paths[0].Points[i],edited.Paths[0].Points[i]);
            Assert.All(edited.Paths,p=>Assert.True(p.Closed));
        }
        [Fact]
        public void Text_MissingOriginalFont_ExplicitReplacementPreservesSavedAffinePose()
        {
            var item=CanvasObjects.Text(new TextObject{Content="Árbol\nO",SizeMm=10},new PointF(10,20));
            var m=AffineTransform.Rotation(35,new PointF(15,20)).Then(AffineTransform.Shear(18,0,PointF.Empty));
            foreach(var path in item.Paths)AffineTransform.Path(path,m);
            item.Text.FontFamily="__missing_original_font__";
            var replacement=item.Text.Clone();replacement.FontFamily="Segoe UI";replacement.Content+=" nuevo";
            var edited=CanvasObjects.Text(replacement,PointF.Empty,item);
            for(int i=0;i<item.Paths[0].Points.Count;i++)Near(item.Paths[0].Points[i],edited.Paths[0].Points[i]);
            Assert.Equal("Segoe UI",edited.Text.FontFamily);Assert.Equal(item.Id,edited.Id);
        }
        [Fact]
        public void V4RoundTrip_RasterMatrixAndTextPose_PersistAndCompileSameGeometry()
        {
            var layer=new LaserLayer();layer.VectorItems.Add(CanvasObjects.Text(new TextObject{Content="Árbol\nO"},new PointF(10,20)));
            AffineTransform.Rotation(90,PointF.Empty).Then(AffineTransform.Shear(12,0,PointF.Empty)).SetLayer(layer);
            var raster=new LaserLayer{ContentKind=LayerContentKind.Raster,Mode=LayerMode.Image,Color=Color.Blue,RasterItem=new RasterItem{BaseCommands={"G90","G0 X0 Y0","M4 S500","G1 X10 Y0","M5"}}};
            AffineTransform.Rotation(45,new PointF(2,3)).SetLayer(raster);var doc=new LaserDocument{Layers={layer,raster}};
            string file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".lps");
            try
            {
                LayerProjectSerializer.Save(file,doc);var loaded=LayerProjectSerializer.Load(file);Assert.Equal(4,loaded.Version);
                Assert.Equal(layer.CrossX,loaded.Layers[0].CrossX);Assert.Equal(raster.CrossY,loaded.Layers[1].CrossY);Assert.Equal(layer.VectorItems[0].Id,loaded.Layers[0].VectorItems[0].Id);
                Assert.Equal(LayeredJobCompiler.Compile(doc,1000,"M4","M5").OutputCommands.Select(c=>c.Command),LayeredJobCompiler.Compile(loaded,1000,"M4","M5").OutputCommands.Select(c=>c.Command));
            }
            finally{File.Delete(file);}
        }
        [Fact]
        public void Raster_ModalPartialAxesAndRelativeMovement_RotateUsingBothCoordinates()
        {
            var commands=new[]{"G90","G0 X2 Y3","M4 S500","G1 X5 F777","Y7 S250","G91","X2","M5"};
            var transformed=RasterAffine.Commands(commands,AffineTransform.Rotation(90,PointF.Empty)).ToArray();
            Assert.Contains("X-3 Y5",transformed[3]);Assert.Contains("F777",transformed[3]);Assert.Contains("S250",transformed[4]);Assert.Contains("X-7 Y5",transformed[4]);
            Assert.Contains("X0 Y2",transformed[6]);Assert.Equal("M4 S500",transformed[2]);Assert.Equal("M5",transformed[7]);
        }
        [Theory]
        [InlineData("G2 X0 Y10 I0 J5")][InlineData("G3 X0 Y10 R5")]
        public void Raster_TransformedArc_IsLinearizedWithExactEndpointAndPower(string arc)
        {
            var m=AffineTransform.Shear(20,0,PointF.Empty);var result=RasterAffine.Commands(new[]{"G90","G0 X0 Y0","M4 S400",arc+" F600","M5"},m).ToList();
            Assert.Contains("M4 S400",result);Assert.Equal("M5",result.Last());Assert.True(result.Count>8);
            Assert.DoesNotContain(result,s=>s.StartsWith("G2 ")||s.StartsWith("G3 "));Assert.Contains("F600",result[3]);
            Assert.Contains("X3.639702 Y10",result[result.Count-2]);
        }
        [Fact]
        public void Scene_PaintsPendingVectorWithoutCompiledCommands()
        {
            var layer=new LaserLayer{Color=Color.Red};layer.VectorItems.Add(CanvasObjects.Shape(CanvasShape.Rectangle,new PointF(10,10),new PointF(30,30)));
            using(var scene=new CanvasScene())using(var bitmap=new Bitmap(200,200))using(var g=Graphics.FromImage(bitmap))
            {
                scene.Draw(g,new LaserDocument{Layers={layer}},p=>new PointF(p.X*4,200-p.Y*4),p=>new PointF(p.X/4,(200-p.Y)/4),bitmap.Size);
                var pixel=bitmap.GetPixel(40,100);Assert.True(pixel.R>pixel.G+60 && pixel.R>pixel.B+60);
            }
        }
        [Theory]
        [InlineData(ColorScheme.Scheme.FadeDark)][InlineData(ColorScheme.Scheme.FadeLight)]
        public void Scene_LowContrastLayer_GetsVisibleEdgeWithoutChangingCutColor(ColorScheme.Scheme scheme)
        {
            var before=ColorScheme.CurrentScheme;
            try
            {
                ColorScheme.CurrentScheme=scheme;
                var color=scheme==ColorScheme.Scheme.FadeDark?ColorScheme.BrandGraphite:Color.White;
                var layer=new LaserLayer{Color=color};layer.VectorItems.Add(CanvasObjects.Shape(CanvasShape.Rectangle,new PointF(10,10),new PointF(30,30)));
                using(var scene=new CanvasScene())using(var bitmap=new Bitmap(200,200))using(var g=Graphics.FromImage(bitmap))
                {
                    scene.Draw(g,new LaserDocument{Layers={layer}},p=>new PointF(p.X*4,200-p.Y*4),p=>new PointF(p.X/4,(200-p.Y)/4),bitmap.Size);
                    var edge=bitmap.GetPixel(39,100);var paper=ColorScheme.FormBackColor;
                    Assert.True(Math.Abs(edge.R-paper.R)+Math.Abs(edge.G-paper.G)+Math.Abs(edge.B-paper.B)>150);
                    Assert.Equal(color.ToArgb(),layer.ColorArgb);
                }
            }
            finally{ColorScheme.CurrentScheme=before;}
        }
    }
}
