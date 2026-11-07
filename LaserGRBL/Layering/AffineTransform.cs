using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LaserGRBL.Layering
{
    [Serializable]
    public sealed class AffineTransform
    {
        public double A { get; set; } = 1;
        public double B { get; set; }
        public double C { get; set; }
        public double D { get; set; } = 1;
        public double X { get; set; }
        public double Y { get; set; }
        public double Determinant { get { return A * D - B * C; } }
        public bool Valid { get { return new[] { A, B, C, D, X, Y }.All(v => !double.IsNaN(v) && !double.IsInfinity(v)) && Math.Abs(Determinant) > 1e-10; } }
        public AffineTransform Clone() { return (AffineTransform)MemberwiseClone(); }
        public LaserPoint Apply(double x, double y) { return new LaserPoint(A * x + C * y + X, B * x + D * y + Y); }
        public PointF Apply(PointF p) { var q = Apply(p.X, p.Y); return new PointF((float)q.X, (float)q.Y); }
        // this * right: right acts first.
        public AffineTransform Then(AffineTransform right)
        {
            return new AffineTransform { A = A*right.A+C*right.B, B = B*right.A+D*right.B,
                C = A*right.C+C*right.D, D = B*right.C+D*right.D,
                X = A*right.X+C*right.Y+X, Y = B*right.X+D*right.Y+Y };
        }
        public AffineTransform Inverse()
        {
            if (!Valid) throw new InvalidOperationException("Singular transform");
            double k = Determinant;
            return new AffineTransform { A=D/k, B=-B/k, C=-C/k, D=A/k, X=(C*Y-D*X)/k, Y=(B*X-A*Y)/k };
        }
        public static AffineTransform Translation(double x, double y) { return new AffineTransform { X=x,Y=y }; }
        public static AffineTransform Around(AffineTransform matrix, PointF pivot) { return Translation(pivot.X,pivot.Y).Then(matrix).Then(Translation(-pivot.X,-pivot.Y)); }
        public static AffineTransform Rotation(double degrees, PointF pivot)
        {
            double t=degrees*Math.PI/180, c=Math.Cos(t),s=Math.Sin(t);
            return Around(new AffineTransform { A=c,B=s,C=-s,D=c },pivot);
        }
        public static AffineTransform Shear(double x, double y, PointF pivot) { return Around(new AffineTransform { C=Math.Tan(x*Math.PI/180),B=Math.Tan(y*Math.PI/180) },pivot); }
        public static AffineTransform Layer(LaserLayer l) { return new AffineTransform { A=l.ScaleX,D=l.ScaleY,C=l.CrossX,B=l.CrossY,X=l.TranslateX,Y=l.TranslateY }; }
        public void SetLayer(LaserLayer l) { l.ScaleX=A;l.ScaleY=D;l.CrossX=C;l.CrossY=B;l.TranslateX=X;l.TranslateY=Y; }
        public static void Path(VectorPath path, AffineTransform matrix)
        {
            path.Points=path.Points.Select(p=>matrix.Apply(p.X,p.Y)).ToList();
            path.Pose=matrix.Then(path.Pose ?? new AffineTransform());
        }
        public static void WorldPath(VectorSelection o, AffineTransform matrix) { var l=Layer(o.Layer);Path(o.Path,l.Inverse().Then(matrix).Then(l)); }
        public static void Selection(LaserDocument document, CanvasSelectionState selection, AffineTransform matrix)
        {
            if (!matrix.Valid) throw new ArgumentException("Invalid transform");
            foreach (var unit in CanvasLayout.Units(document,selection.Objects,false).Where(u=>u.IsSelected(selection)))
            {
                foreach (var layer in unit.Layers) matrix.Then(Layer(layer)).SetLayer(layer);
                foreach (var o in unit.Objects) WorldPath(o,matrix);
            }
        }
        // Fit the retained glyph contours, including transformations applied by older projects.
        public static AffineTransform Fit(IList<LaserPoint> source, IList<LaserPoint> target)
        {
            if (source.Count!=target.Count || source.Count<3) return null;
            int j=Enumerable.Range(1,source.Count-1).OrderByDescending(n=>Math.Pow(source[n].X-source[0].X,2)+Math.Pow(source[n].Y-source[0].Y,2)).First();
            int best=Enumerable.Range(1,source.Count-1).OrderByDescending(n=>Math.Abs((source[j].X-source[0].X)*(source[n].Y-source[0].Y)-(source[j].Y-source[0].Y)*(source[n].X-source[0].X))).First();
            foreach(int k in new[]{best})
            {
                var s=new AffineTransform { A=source[j].X-source[0].X,B=source[j].Y-source[0].Y,C=source[k].X-source[0].X,D=source[k].Y-source[0].Y,X=source[0].X,Y=source[0].Y };
                if(!s.Valid) continue;
                return new AffineTransform { A=target[j].X-target[0].X,B=target[j].Y-target[0].Y,C=target[k].X-target[0].X,D=target[k].Y-target[0].Y,X=target[0].X,Y=target[0].Y }.Then(s.Inverse());
            }
            return null;
        }
    }

    public static class RasterAffine
    {
        private static readonly Regex Words=new Regex(@"([A-Z])\s*([-+]?(?:\d+\.?\d*|\.\d+)(?:E[-+]?\d+)?)",RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private static string N(double v) { return v.ToString("0.######",CultureInfo.InvariantCulture); }
        public static IEnumerable<string> Commands(IEnumerable<string> commands, AffineTransform matrix)
        {
            bool absolute=true, absoluteCenter=false; double x=0,y=0;int motion=0;
            foreach(var original in commands ?? Enumerable.Empty<string>())
            {
                string line=original ?? ""; int comment=line.IndexOf(';'); string code=comment<0?line:line.Substring(0,comment);
                code=Regex.Replace(code,@"\([^)]*\)","");
                var words=Words.Matches(code).Cast<Match>().ToList();
                foreach(var g in words.Where(w=>w.Groups[1].Value.Equals("G",StringComparison.OrdinalIgnoreCase)))
                {
                    double v=double.Parse(g.Groups[2].Value,CultureInfo.InvariantCulture);
                    if(v==90) absolute=true;else if(v==91) absolute=false;else if(v==90.1) absoluteCenter=true;else if(v==91.1) absoluteCenter=false;
                    else if(v>=0 && v<=3 && v==(int)v) motion=(int)v;
                }
                Func<string,double?> get=a=> { var w=words.LastOrDefault(m=>m.Groups[1].Value.Equals(a,StringComparison.OrdinalIgnoreCase));return w==null?(double?)null:double.Parse(w.Groups[2].Value,CultureInfo.InvariantCulture); };
                var xx=get("X");var yy=get("Y");var ii=get("I");var jj=get("J");var rr=get("R");
                if(!xx.HasValue&&!yy.HasValue&&!(motion>=2&&(ii.HasValue||jj.HasValue||rr.HasValue))) { yield return line;continue; }
                double nx=absolute?(xx??x):x+(xx??0),ny=absolute?(yy??y):y+(yy??0);
                if(motion<2 && Math.Abs(matrix.B)<1e-12 && Math.Abs(matrix.C)<1e-12)
                {
                    yield return Words.Replace(line,w=> {
                        string axis=w.Groups[1].Value.ToUpperInvariant();
                        if(axis!="X"&&axis!="Y")return w.Value;
                        double v=double.Parse(w.Groups[2].Value,CultureInfo.InvariantCulture);
                        return axis+N(v*(axis=="X"?matrix.A:matrix.D)+(absolute?(axis=="X"?matrix.X:matrix.Y):0));
                    });
                    x=nx;y=ny;continue;
                }
                var start=matrix.Apply(x,y);
                string extra=Words.Replace(code,m=> {
                    string a=m.Groups[1].Value.ToUpperInvariant();double v=double.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture);
                    return a=="X"||a=="Y"||a=="I"||a=="J"||a=="R"||(a=="G"&&v>=0&&v<=3)?"":m.Value;
                }).Trim();
                if(motion<2) { var p=matrix.Apply(nx,ny);yield return "G"+motion+(extra.Length==0?"":" "+extra)+" X"+N(absolute?p.X:p.X-start.X)+" Y"+N(absolute?p.Y:p.Y-start.Y); }
                else
                {
                    double cx=x+(ii??0),cy=y+(jj??0);
                    if(absoluteCenter) {cx=ii??x;cy=jj??y;}
                    if(rr.HasValue)
                    {
                        double dx=nx-x,dy=ny-y,chord=Math.Sqrt(dx*dx+dy*dy),r=Math.Abs(rr.Value);
                        if(chord<1e-12 || chord>2*r+1e-8) throw new InvalidOperationException("Invalid raster arc");
                        double h=Math.Sqrt(Math.Max(0,r*r-chord*chord/4));double sign=(motion==2?-1:1)*(rr.Value<0?-1:1);
                        cx=(x+nx)/2-sign*dy*h/chord;cy=(y+ny)/2+sign*dx*h/chord;
                    }
                    double radius=Math.Sqrt((x-cx)*(x-cx)+(y-cy)*(y-cy)),a=Math.Atan2(y-cy,x-cx),sweep=Math.Atan2(ny-cy,nx-cx)-a;
                    if(motion==2) {while(sweep>=0)sweep-=2*Math.PI;}else {while(sweep<=0)sweep+=2*Math.PI;}
                    double scale=Math.Sqrt(matrix.A*matrix.A+matrix.B*matrix.B+matrix.C*matrix.C+matrix.D*matrix.D);
                    double step=2*Math.Acos(Math.Max(-1,Math.Min(1,1-.01/Math.Max(.01,radius*scale))));
                    int count=Math.Max(1,(int)Math.Ceiling(Math.Abs(sweep)/Math.Max(.00001,step)));
                    if(count>1000000)throw new InvalidOperationException("Raster arc exceeds limit");
                    for(int n=1;n<=count;n++) {double t=a+sweep*n/count;var p=matrix.Apply(n==count?nx:cx+radius*Math.Cos(t),n==count?ny:cy+radius*Math.Sin(t));yield return "G1 "+(n==1?extra:"")+" X"+N(absolute?p.X:p.X-start.X)+" Y"+N(absolute?p.Y:p.Y-start.Y);start=p;}
                    // Restore modal arc movement for a following implicit arc in source.
                }
                x=nx;y=ny;
            }
        }
    }
}
