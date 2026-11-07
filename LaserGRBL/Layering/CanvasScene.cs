using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;

namespace LaserGRBL.Layering
{
    // Both canvas implementations draw editable geometry without waiting for G-code.
    public sealed class CanvasScene : IDisposable
    {
        private readonly Dictionary<RasterItem,Bitmap> images=new Dictionary<RasterItem,Bitmap>();
        private static double Luminance(Color color)
        {
            Func<byte,double> linear=v=>v<=10?v/3294.6:Math.Pow((v/255.0+.055)/1.055,2.4);
            return .2126*linear(color.R)+.7152*linear(color.G)+.0722*linear(color.B);
        }
        public void Dispose() { foreach(var b in images.Values)b.Dispose();images.Clear(); }
        public void Draw(Graphics g, LaserDocument document, Func<PointF,PointF> toClient, Func<PointF,PointF> toWorld, Size size)
        {
            g.Clear(ColorScheme.FormBackColor);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            var first=toWorld(PointF.Empty);var last=toWorld(new PointF(size.Width,size.Height));
            double left=Math.Min(first.X,last.X),right=Math.Max(first.X,last.X),bottom=Math.Min(first.Y,last.Y),top=Math.Max(first.Y,last.Y);
            double spacing=Math.Pow(10,Math.Floor(Math.Log10(Math.Max(.001,right-left)/12)));
            if((right-left)/spacing>40)spacing*=5;
            using(var grid=new Pen(Color.FromArgb(35,ColorScheme.FormForeColor)))
            {
                for(double x=Math.Floor(left/spacing)*spacing;x<=right;x+=spacing)g.DrawLine(grid,toClient(new PointF((float)x,(float)bottom)),toClient(new PointF((float)x,(float)top)));
                for(double y=Math.Floor(bottom/spacing)*spacing;y<=top;y+=spacing)g.DrawLine(grid,toClient(new PointF((float)left,(float)y)),toClient(new PointF((float)right,(float)y)));
            }
            using(var axes=new Pen(Color.FromArgb(130,ColorScheme.FormForeColor)))
            {
                g.DrawLine(axes,toClient(new PointF((float)left,0)),toClient(new PointF((float)right,0)));
                g.DrawLine(axes,toClient(new PointF(0,(float)bottom)),toClient(new PointF(0,(float)top)));
            }
            var active=new HashSet<RasterItem>(document.Layers.Where(l=>l.RasterItem!=null).Select(l=>l.RasterItem));
            foreach(var old in images.Keys.Where(k=>!active.Contains(k)).ToList()){images[old].Dispose();images.Remove(old);}
            foreach(var layer in document.Layers.Where(l=>l.Visible).OrderBy(l=>l.Order))
            {
                if(layer.ContentKind==LayerContentKind.Raster)
                {
                    var r=layer.RasterItem;if(r==null||r.ImageData==null)continue;
                    Bitmap image;
                    if(!images.TryGetValue(r,out image)) {using(var stream=new MemoryStream(r.ImageData))using(var source=Image.FromStream(stream))image=new Bitmap(source);images.Add(r,image);}
                    var points=LayerGeometry.ReadRasterPoints(r.BaseCommands);if(points.Count==0)continue;
                    float x=(float)points.Min(p=>p.X),y=(float)points.Min(p=>p.Y),w=(float)(points.Max(p=>p.X)-x),h=(float)(points.Max(p=>p.Y)-y);
                    var m=AffineTransform.Layer(layer);
                    g.DrawImage(image,new[]{toClient(m.Apply(new PointF(x,y+h))),toClient(m.Apply(new PointF(x+w,y+h))),toClient(m.Apply(new PointF(x,y)))});
                    continue;
                }
                var color=layer.OutputEnabled?layer.Color:Color.FromArgb(110,layer.Color);
                double ink=Luminance(layer.Color),paper=Luminance(ColorScheme.FormBackColor);
                bool contrastEdge=(Math.Max(ink,paper)+.05)/(Math.Min(ink,paper)+.05)<3;
                using(var pen=new Pen(color,1.3f))
                using(var edge=new Pen(ColorScheme.FormForeColor,3.3f))
                using(var brush=new SolidBrush(Color.FromArgb(75,color)))
                using(var fill=new GraphicsPath(FillMode.Alternate))
                {
                    foreach(var path in layer.VectorItems.SelectMany(i=>i.Paths).Where(p=>p.Points.Count>1))
                    {
                        var p=LayerGeometry.Transform(path,layer).Points.Select(v=>toClient(new PointF((float)v.X,(float)v.Y))).ToArray();
                        if(contrastEdge){if(path.Closed)g.DrawPolygon(edge,p);else g.DrawLines(edge,p);}
                        if(path.Closed){g.DrawPolygon(pen,p);if(p.Length>=3)fill.AddPolygon(p);}else g.DrawLines(pen,p);
                    }
                    if((layer.Mode==LayerMode.Fill||layer.Mode==LayerMode.OffsetFill)&&fill.PointCount>0)g.FillPath(brush,fill);
                }
            }
        }
    }
}
