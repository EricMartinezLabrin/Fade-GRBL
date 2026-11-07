using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using LaserGRBL.Layering;
using LaserGRBL.UserControls;

namespace LaserGRBL
{
    public partial class PreviewForm
    {
        private ToolStrip TransformToolbar, TextToolbar;
        private ToolStripTextBox AngleBox, ShearXBox, ShearYBox, LengthBox;
        private ToolStripLabel AngleLabel;
        private readonly HashSet<ToolStripTextBox> TransformEdited=new HashSet<ToolStripTextBox>();
        private bool UpdatingTransformFields;
        private PointF? CanvasPivot;
        private string PivotSelectionKey;
        private int PivotPosition=4;
        private string AffineGesture;
        private PointF AffineStart, AffinePivot;
        private RectangleF AffineBounds;
        private readonly Dictionary<VectorSelection,VectorPath> AffinePaths=new Dictionary<VectorSelection,VectorPath>();
        private readonly Dictionary<LaserLayer,AffineTransform> AffineLayers=new Dictionary<LaserLayer,AffineTransform>();
        private float HandleScale { get { return Math.Max(.75f,(GrblPanel as Control).DeviceDpi/96f); } }
        private static readonly string[] HandleNames={"nw","n","ne","w","e","sw","s","se"};

        private void BuildDirectEditingTools()
        {
            TransformToolbar=new ToolStrip { Dock=DockStyle.Fill,GripStyle=ToolStripGripStyle.Hidden,MinimumSize=new Size(0,32) };
            var pivot=new ToolStripDropDownButton(LayerText.Get("Pivot"));
            for(int i=0;i<9;i++)
            {
                int index=i;var item=new ToolStripMenuItem(LayerText.Get("Pivot"+i));
                item.Click+=(s,e)=>{PivotPosition=index;CanvasPivot=null;(GrblPanel as Control).Invalidate();};pivot.DropDownItems.Add(item);
            }
            TransformToolbar.Items.Add(pivot);
            AngleLabel=new ToolStripLabel(LayerText.Get("Angle"));TransformToolbar.Items.Add(AngleLabel);
            AngleBox=TransformField("Angle");
            TransformToolbar.Items.Add(new ToolStripLabel(LayerText.Get("ShearX")));ShearXBox=TransformField("ShearX");
            TransformToolbar.Items.Add(new ToolStripLabel(LayerText.Get("ShearY")));ShearYBox=TransformField("ShearY");
            TransformToolbar.Items.Add(new ToolStripLabel(LayerText.Get("LineLength")));LengthBox=TransformField("LineLength");
            BuildInlineTextTools();
            FadeTheme.Apply(TransformToolbar);FadeTheme.Apply(TextToolbar);
        }
        private ToolStripTextBox TransformField(string key)
        {
            var box=new ToolStripTextBox { AutoSize=false,Width=60,ToolTipText=LayerText.Get(key),AccessibleName=LayerText.Get(key) };
            box.TextChanged+=(s,e)=>{if(!UpdatingTransformFields)TransformEdited.Add(box);};
            box.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){ApplyTransformField(box);e.Handled=true;e.SuppressKeyPress=true;}};
            box.Leave+=(s,e)=>ApplyTransformField(box);TransformToolbar.Items.Add(box);return box;
        }
        private void SetTransformField(ToolStripTextBox box,double value)
        {
            if(TransformEdited.Contains(box))return;
            UpdatingTransformFields=true;try{box.ForeColor=ColorScheme.FormForeColor;box.ToolTipText=box.AccessibleName;box.Text=value.ToString("0.###",CultureInfo.CurrentCulture);box.TextBox.ClearUndo();}finally{UpdatingTransformFields=false;}
        }
        private VectorSelection SingleLine()
        {
            VectorSelection o;
            if(ObjectModeButton.Checked)
            {
                if(SelectedObjects.Count!=1||GroupRasterObjects().Count!=0)return null;
                o=SelectedObjects[0];
            }
            else
            {
                if(SelectedLayers.Count!=1||SelectedLayers[0].ContentKind!=LayerContentKind.Vector)return null;
                var layer=SelectedLayers[0];var paths=layer.VectorItems.SelectMany(i=>i.Paths).ToList();
                if(paths.Count!=1)return null;var item=layer.VectorItems.First(i=>i.Paths.Contains(paths[0]));
                o=new VectorSelection(layer,item,paths[0]);
            }
            return o.Item.Text==null&&!o.Path.Closed&&o.Path.Points.Count==2?o:null;
        }
        private double SelectionAngle()
        {
            var line=SingleLine();
            if(line!=null){var p=LayerGeometry.Transform(line.Path,line.Layer).Points;return Math.Atan2(p[1].Y-p[0].Y,p[1].X-p[0].X)*180/Math.PI;}
            var units=CanvasLayout.Units(mCore.LayerController.Document,ObjectModeButton.Checked,false).Where(u=>u.IsSelected(CurrentCanvasSelection())).ToList();
            if(units.Count!=1)return 0;
            AffineTransform m;
            if(units[0].Objects.Count>0){var o=units[0].Objects[0];m=AffineTransform.Layer(o.Layer).Then(o.Path.Pose);}
            else m=AffineTransform.Layer(units[0].Layers[0]);
            return Math.Atan2(m.B,m.A)*180/Math.PI;
        }
        private bool MultipleTransformUnits()
        {
            return CanvasLayout.Units(mCore.LayerController.Document,ObjectModeButton.Checked,false).Count(u=>u.IsSelected(CurrentCanvasSelection()))>1;
        }
        private void RefreshTransformTools(bool enabled)
        {
            if(TransformToolbar==null)return;
            TransformToolbar.Enabled=enabled&&InlineText==null;
            string key=string.Join(",",CurrentCanvasSelection().Paths.Concat(CurrentCanvasSelection().Layers).OrderBy(id=>id).Select(id=>id.ToString()).ToArray());
            if(key!=PivotSelectionKey){PivotSelectionKey=key;CanvasPivot=null;TransformEdited.Clear();}
            if(enabled)
            {
                AngleLabel.Text=LayerText.Get(MultipleTransformUnits()?"RotateSelection":"Angle");
                SetTransformField(AngleBox,SelectionAngle());SetTransformField(ShearXBox,0);SetTransformField(ShearYBox,0);
                var line=SingleLine();LengthBox.Enabled=line!=null;
                if(line!=null){var p=LayerGeometry.Transform(line.Path,line.Layer).Points;SetTransformField(LengthBox,Math.Sqrt(Math.Pow(p[1].X-p[0].X,2)+Math.Pow(p[1].Y-p[0].Y,2)));}
            }
            RefreshInlineTextTools();
        }
        private PointF TransformPivot(RectangleF b)
        {
            return CanvasPivot??new PointF(b.Left+b.Width*(PivotPosition%3)/2,b.Bottom-b.Height*(PivotPosition/3)/2);
        }
        private void ApplyTransformField(ToolStripTextBox box)
        {
            if(!TransformEdited.Contains(box)||mCore==null||mCore.JobEditingLocked)return;
            double v;RectangleF b;
            if(!TryReadDimension(box.Text,out v)||!TrySelectionBounds(out b)||(box==LengthBox&&v<=0)||((box==ShearXBox||box==ShearYBox)&&Math.Abs(v)>=89.9))
            {box.ForeColor=ColorScheme.PreviewCommandKO;box.ToolTipText=LayerText.Get("InvalidValue");return;}
            AffineTransform m;
            if(box==LengthBox)
            {
                var line=SingleLine();if(line==null)return;
                var p=LayerGeometry.Transform(line.Path,line.Layer).Points;double len=Math.Sqrt(Math.Pow(p[1].X-p[0].X,2)+Math.Pow(p[1].Y-p[0].Y,2));if(len<1e-9)return;
                m=AffineTransform.Around(new AffineTransform { A=v/len,D=v/len },new PointF((float)p[0].X,(float)p[0].Y));
            }
            else if(box==AngleBox)m=AffineTransform.Rotation(v-(MultipleTransformUnits()?0:SelectionAngle()),TransformPivot(b));
            else m=AffineTransform.Shear(box==ShearXBox?v:0,box==ShearYBox?v:0,TransformPivot(b));
            if(!m.Valid){box.ForeColor=ColorScheme.PreviewCommandKO;return;}
            TransformEdited.Remove(box);box.ForeColor=ColorScheme.FormForeColor;box.ToolTipText=box.AccessibleName;
            AffineTransform.Selection(mCore.LayerController.Document,CurrentCanvasSelection(),m);CommitCanvasTransform();
            // Incremental fields reset after acceptance, including while focus remains here.
            if(box==ShearXBox||box==ShearYBox||(box==AngleBox&&MultipleTransformUnits())){UpdatingTransformFields=true;box.Text="0";UpdatingTransformFields=false;}
            RefreshSelectionEditor();
        }
        private Dictionary<string,PointF> TransformHandles()
        {
            var result=new Dictionary<string,PointF>();RectangleF b;
            if(mCore==null||!mCore.LayerController.IsLayered||mCore.JobEditingLocked||InlineText!=null||!TrySelectionBounds(out b))return result;
            var a=GrblPanel.MachineToClient(new PointF(b.Left,b.Bottom));var z=GrblPanel.MachineToClient(new PointF(b.Right,b.Top));
            float l=Math.Min(a.X,z.X),r=Math.Max(a.X,z.X),t=Math.Min(a.Y,z.Y),d=Math.Max(a.Y,z.Y),cx=(l+r)/2,cy=(t+d)/2;
            var locations=new[]{new PointF(l,t),new PointF(cx,t),new PointF(r,t),new PointF(l,cy),new PointF(r,cy),new PointF(l,d),new PointF(cx,d),new PointF(r,d)};
            for(int i=0;i<locations.Length;i++)result[HandleNames[i]]=locations[i];
            result["rotate"]=new PointF(cx,t-28*HandleScale);result["shearX"]=new PointF(r+22*HandleScale,t);result["shearY"]=new PointF(r+22*HandleScale,cy);
            result["pivot"]=GrblPanel.MachineToClient(TransformPivot(b));
            var line=SingleLine();if(line!=null){result.Clear();var p=LayerGeometry.Transform(line.Path,line.Layer).Points;result["start"]=GrblPanel.MachineToClient(new PointF((float)p[0].X,(float)p[0].Y));result["end"]=GrblPanel.MachineToClient(new PointF((float)p[1].X,(float)p[1].Y));result["rotate"]=new PointF(cx,t-28*HandleScale);result["pivot"]=GrblPanel.MachineToClient(TransformPivot(b));}
            return result;
        }
        private void InstallDirectEditing(Control canvas)
        {
            canvas.Paint+=(s,e)=>
            {
                using(var pen=new Pen(ColorScheme.BrandAmber,1.5f))using(var fill=new SolidBrush(ColorScheme.FormBackColor))using(var text=new SolidBrush(ColorScheme.FormForeColor))
                foreach(var h in TransformHandles())
                {
                    var r=new RectangleF(h.Value.X-4*HandleScale,h.Value.Y-4*HandleScale,8*HandleScale,8*HandleScale);
                    if(h.Key=="pivot"||h.Key=="rotate"){e.Graphics.FillEllipse(fill,r);e.Graphics.DrawEllipse(pen,r);}
                    else {e.Graphics.FillRectangle(fill,r);e.Graphics.DrawRectangle(pen,r.X,r.Y,r.Width,r.Height);}
                    if(h.Key=="rotate")e.Graphics.DrawString("↻",Font,text,h.Value.X+5,h.Value.Y-8);
                    if(h.Key=="shearX"||h.Key=="shearY")e.Graphics.DrawString(h.Key=="shearX"?"↔":"↕",Font,text,h.Value.X+5,h.Value.Y-8);
                }
                DrawInlineText(e.Graphics);
            };
            canvas.MouseCaptureChanged+=(s,e)=>{if(!canvas.Capture&&AffineGesture!=null)CancelAffineGesture();};
            canvas.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Middle&&InlineText!=null)InlineText.Focus();};
        }
        private bool BeginAffineGesture(MouseEventArgs e)
        {
            var hit=TransformHandles().Reverse().FirstOrDefault(h=>Math.Abs(h.Value.X-e.X)<=7*HandleScale&&Math.Abs(h.Value.Y-e.Y)<=7*HandleScale);
            if(hit.Key==null)return false;
            AffineGesture=hit.Key;AffineStart=GrblPanel.ClientToMachine(e.Location);TrySelectionBounds(out AffineBounds);AffinePivot=TransformPivot(AffineBounds);
            AffinePaths.Clear();AffineLayers.Clear();
            foreach(var u in CanvasLayout.Units(mCore.LayerController.Document,ObjectModeButton.Checked,false).Where(u=>u.IsSelected(CurrentCanvasSelection())))
            {foreach(var l in u.Layers)AffineLayers[l]=AffineTransform.Layer(l);foreach(var o in u.Objects)AffinePaths[o]=o.Path.Clone();}
            var line=SingleLine();if(line!=null&&!AffinePaths.Keys.Any(o=>o.Path==line.Path))AffinePaths[line]=line.Path.Clone();
            (GrblPanel as Control).Capture=true;return true;
        }
        private void RestoreAffineSnapshot()
        {
            foreach(var kv in AffineLayers)kv.Value.SetLayer(kv.Key);
            foreach(var kv in AffinePaths){kv.Key.Path.Points=kv.Value.Points.Select(p=>p.Clone()).ToList();kv.Key.Path.Pose=kv.Value.Pose.Clone();}
        }
        private bool MoveAffineGesture(MouseEventArgs e)
        {
            if(AffineGesture==null)return false;
            var p=GrblPanel.ClientToMachine(e.Location);var name=AffineGesture;
            if(name=="pivot"){CanvasPivot=p;(GrblPanel as Control).Invalidate();return true;}
            RestoreAffineSnapshot();AffineTransform m;
            if(name=="rotate")
            {
                double angle=(Math.Atan2(p.Y-AffinePivot.Y,p.X-AffinePivot.X)-Math.Atan2(AffineStart.Y-AffinePivot.Y,AffineStart.X-AffinePivot.X))*180/Math.PI;
                if((ModifierKeys&Keys.Shift)!=0)angle=Math.Round(angle/15)*15;m=AffineTransform.Rotation(angle,AffinePivot);
            }
            else if(name=="shearX"||name=="shearY")
            {double x=name=="shearX"?(p.X-AffineStart.X)/Math.Max(.01,AffineBounds.Height):0,y=name=="shearY"?(p.Y-AffineStart.Y)/Math.Max(.01,AffineBounds.Width):0;m=AffineTransform.Around(new AffineTransform{C=x,B=y},AffinePivot);}
            else if(name=="start"||name=="end")
            {
                var o=SingleLine();int index=name=="start"?0:1;var q=AffineTransform.Layer(o.Layer).Inverse().Apply(p);
                o.Path.Points[index].X=q.X;o.Path.Points[index].Y=q.Y;m=null;
            }
            else
            {
                bool west=name.Contains("w"),east=name.Contains("e"),north=name.Contains("n"),south=name.Contains("s");
                double ax=west?AffineBounds.Right:AffineBounds.Left,ay=north?AffineBounds.Top:AffineBounds.Bottom;
                double sx=(west||east)&&AffineBounds.Width>1e-9?(p.X-ax)/(AffineStart.X-ax):1;
                double sy=(north||south)&&AffineBounds.Height>1e-9?(p.Y-ay)/(AffineStart.Y-ay):1;
                if(LockAspectButton.Checked){double k=Math.Abs(sx-1)>Math.Abs(sy-1)?sx:sy;sx=sy=k;}
                m=AffineTransform.Around(new AffineTransform{A=sx,D=sy},new PointF((float)ax,(float)ay));
            }
            if(m!=null&&m.Valid)AffineTransform.Selection(mCore.LayerController.Document,CurrentCanvasSelection(),m);
            RefreshSelectionEditor();mCore.LayerController.PreviewChanged();return true;
        }
        private bool EndAffineGesture(MouseEventArgs e)
        {
            if(AffineGesture==null||e.Button!=MouseButtons.Left)return false;
            bool pivot=AffineGesture=="pivot";AffineGesture=null;(GrblPanel as Control).Capture=false;
            if(!pivot)CommitCanvasTransform();return true;
        }
        private bool CancelAffineGesture()
        {
            if(AffineGesture==null)return false;
            bool pivot=AffineGesture=="pivot";AffineGesture=null;(GrblPanel as Control).Capture=false;
            if(pivot){CanvasPivot=AffinePivot;(GrblPanel as Control).Invalidate();}else mCore.LayerController.CancelEdit();
            return true;
        }
    }
}
