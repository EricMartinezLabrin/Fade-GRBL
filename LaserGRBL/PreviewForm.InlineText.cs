using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using LaserGRBL.Layering;

namespace LaserGRBL
{
    public partial class PreviewForm
    {
        private RichTextBox InlineText;
        private ToolStripComboBox FontBox, FontStyleBox, TextAlignmentBox;
        private ToolStripTextBox FontSizeBox;
        private ToolStripLabel TextError;
        private bool UpdatingTextTools, FinishingText, InlineWasNew;
        private LaserLayer InlineLayer;
        private VectorItem InlineItem, InlineReference;
        private TextObject InlineMetadata;
        private PointF InlineAnchor;
        private AffineTransform InlineGlyphPose;
        private Timer CaretTimer;
        private int InlinePointerAnchor=-1;

        private void DiscardInlineInput()
        {
            if(InlineText==null)return;
            var input=InlineText;InlineText=null;
            if(CaretTimer!=null){CaretTimer.Stop();CaretTimer.Dispose();CaretTimer=null;}
            InlinePointerAnchor=-1;InlineItem=InlineReference=null;InlineMetadata=null;InlineGlyphPose=null;if(!input.IsDisposed)input.Dispose();
        }

        private void BuildInlineTextTools()
        {
            TextToolbar=new ToolStrip { Dock=DockStyle.Fill,GripStyle=ToolStripGripStyle.Hidden,MinimumSize=new Size(0,32),Visible=false };
            FontBox=new ToolStripComboBox { AutoSize=false,Width=180,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName=LayerText.Get("FontFamily") };
            FontBox.Items.AddRange(FontFamily.Families.Select(f=>(object)f.Name).OrderBy(n=>n.ToString()).ToArray());
            FontStyleBox=new ToolStripComboBox { AutoSize=false,Width=110,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName=LayerText.Get("FontStyle") };
            FontSizeBox=new ToolStripTextBox { AutoSize=false,Width=60,AccessibleName=LayerText.Get("TextSize") };
            TextAlignmentBox=new ToolStripComboBox { AutoSize=false,Width=95,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName=LayerText.Get("TextAlignment") };
            TextAlignmentBox.Items.AddRange(new object[]{LayerText.Get("AlignLeft"),LayerText.Get("AlignCenter"),LayerText.Get("AlignRight")});
            TextError=new ToolStripLabel { ForeColor=ColorScheme.PreviewCommandKO };
            TextToolbar.Items.AddRange(new ToolStripItem[]{new ToolStripLabel(LayerText.Get("FontFamily")),FontBox,FontStyleBox,new ToolStripLabel(LayerText.Get("TextSize")),FontSizeBox,TextAlignmentBox,TextError});
            FontBox.SelectedIndexChanged+=(s,e)=>{if(!UpdatingTextTools){RefreshAvailableStyles();ApplyInlineOptions();}};
            FontStyleBox.SelectedIndexChanged+=(s,e)=>ApplyInlineOptions();TextAlignmentBox.SelectedIndexChanged+=(s,e)=>ApplyInlineOptions();
            FontSizeBox.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){ApplyInlineOptions();e.Handled=true;e.SuppressKeyPress=true;}};
            FontSizeBox.Leave+=(s,e)=>ApplyInlineOptions();
        }
        private sealed class FontStyleChoice
        {
            public FontStyle Style;public string Label;public override string ToString(){return Label;}
        }
        private void RefreshAvailableStyles()
        {
            if(FontBox.SelectedItem==null)return;
            bool before=UpdatingTextTools;UpdatingTextTools=true;
            var prior=FontStyleBox.SelectedItem as FontStyleChoice;FontStyle wanted=prior==null?FontStyle.Regular:prior.Style;
            FontStyleBox.Items.Clear();
            using(var family=new FontFamily(FontBox.SelectedItem.ToString()))
                foreach(var style in new[]{FontStyle.Regular,FontStyle.Bold,FontStyle.Italic,FontStyle.Bold|FontStyle.Italic})
                    if(family.IsStyleAvailable(style))FontStyleBox.Items.Add(new FontStyleChoice{Style=style,Label=LayerText.Get("Style"+(int)style)});
            FontStyleBox.SelectedItem=FontStyleBox.Items.Cast<FontStyleChoice>().FirstOrDefault(v=>v.Style==wanted);
            if(FontStyleBox.SelectedIndex<0&&FontStyleBox.Items.Count>0)FontStyleBox.SelectedIndex=0;
            UpdatingTextTools=before;
        }
        private void RefreshInlineTextTools()
        {
            if(TextToolbar==null)return;
            bool selected=SelectedObjects.Any(o=>o.Item.Text!=null);
            TextToolbar.Visible=InlineText!=null||ActiveCanvasTool==CanvasShape.Text||selected;
            if(InlineText!=null||TextToolbar.ContainsFocus)return;
            var metadata=SelectedObjects.Where(o=>o.Item.Text!=null).Select(o=>o.Item.Text).FirstOrDefault()??PendingText??new TextObject();
            SetInlineOptions(metadata);
        }
        private void SetInlineOptions(TextObject value)
        {
            UpdatingTextTools=true;
            try
            {
                FontBox.SelectedItem=value.FontFamily;RefreshAvailableStyles();
                FontStyleBox.SelectedItem=FontStyleBox.Items.Cast<FontStyleChoice>().FirstOrDefault(v=>v.Style==value.Style);
                FontSizeBox.Text=value.SizeMm.ToString("0.###",System.Globalization.CultureInfo.CurrentCulture);
                TextAlignmentBox.SelectedIndex=(int)value.Alignment;
                TextError.Text=FontBox.SelectedIndex<0?LayerText.Get("ChooseMissingFont"):"";
            }
            finally{UpdatingTextTools=false;}
        }
        private void ApplyInlineOptions()
        {
            if(UpdatingTextTools||FinishingText||mCore==null||mCore.JobEditingLocked)return;
            double size;var style=FontStyleBox.SelectedItem as FontStyleChoice;
            if(!TryReadDimension(FontSizeBox.Text,out size)||size<=0||size>1000||FontBox.SelectedIndex<0||style==null){TextError.Text=LayerText.Get("InvalidValue");return;}
            string chosenFont=FontBox.SelectedItem.ToString(); var chosenStyle=style.Style; int chosenAlignment=TextAlignmentBox.SelectedIndex;
            if(InlineText==null)
            {
                var selected=SelectedObjects.FirstOrDefault(o=>o.Item.Text!=null);
                if(selected!=null)BeginInlineText(selected,PointF.Empty);
            }
            var metadata=InlineMetadata??PendingText??new TextObject();
            metadata=metadata.Clone();metadata.FontFamily=chosenFont;metadata.Style=chosenStyle;metadata.SizeMm=size;
            metadata.Alignment=(StringAlignment)Math.Max(0,chosenAlignment);PendingText=metadata.Clone();TextError.Text="";
            if(InlineText!=null){InlineMetadata=metadata;SetInlineOptions(metadata);UpdateInlineGeometry();InlineText.Focus();}
        }
        private void BeginInlineText(VectorSelection selected,PointF anchor)
        {
            if(InlineText!=null)FinishInlineText(true);
            mCore.LayerController.BeginTransientEdit();
            InlineWasNew=selected==null;InlineAnchor=anchor;InlineLayer=selected==null?SelectedLayers.FirstOrDefault(l=>l.ContentKind==LayerContentKind.Vector):selected.Layer;
            InlineItem=selected==null?null:selected.Item;InlineReference=InlineItem==null?null:InlineItem.Clone();
            InlineMetadata=(InlineItem==null?PendingText??new TextObject():InlineItem.Text).Clone();InlineGlyphPose=null;
            SetInlineOptions(InlineMetadata);TextToolbar.Visible=true;
            // Native input retains Windows text navigation, clipboard, IME and text undo.
            // Glyphs, selection and caret are rendered on the canvas with object pose.
            InlineText=new RichTextBox { Multiline=true,WordWrap=false,DetectUrls=false,ScrollBars=RichTextBoxScrollBars.None,AcceptsTab=false,BorderStyle=BorderStyle.None,Size=new Size(1,1),TabStop=false,Text=InlineItem==null?"":InlineItem.Text.Content };
            var pixel=GrblPanel.MachineToClient(anchor);InlineText.Location=new Point(Math.Max(0,(int)pixel.X),Math.Max(0,(int)pixel.Y));
            (GrblPanel as Control).Controls.Add(InlineText);InlineText.BringToFront();
            InlineText.TextChanged+=(s,e)=>UpdateInlineGeometry();
            InlineText.KeyUp+=(s,e)=>(GrblPanel as Control).Invalidate();
            InlineText.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape||(e.Control&&e.KeyCode==Keys.Enter)){bool accept=e.KeyCode!=Keys.Escape;FinishInlineText(accept);e.Handled=true;e.SuppressKeyPress=true;}};
            InlineText.LostFocus+=(s,e)=>
            {
                var editor=s as RichTextBox;
                if(!IsDisposed&&!Disposing&&IsHandleCreated)
                    BeginInvoke((MethodInvoker)(()=>{if(InlineText==editor&&!editor.IsDisposed&&!editor.ContainsFocus&&!TextToolbar.ContainsFocus&&!FinishingText)FinishInlineText(true);}));
            };
            InlineText.Disposed+=(s,e)=>{if(InlineText==s)DiscardInlineInput();};
            CaretTimer=new Timer{Interval=500};CaretTimer.Tick+=(s,e)=>(GrblPanel as Control).Invalidate();CaretTimer.Start();
            InlineText.SelectionStart=InlineText.TextLength;InlineText.Focus();
            // Existing text is not regenerated until its first actual change.
            PrepareInlineGlyphPose();RefreshSelectionEditor();
        }
        private void PrepareInlineGlyphPose()
        {
            if(InlineItem==null||InlineItem.Text==null||!CanvasObjects.FontAvailable(InlineItem.Text.FontFamily))return;
            var canonical=CanvasObjects.Text(InlineItem.Text,PointF.Empty);
            InlineGlyphPose=AffineTransform.Fit(canonical.Paths.SelectMany(p=>p.Points).ToList(),InlineItem.Paths.SelectMany(p=>p.Points).ToList());
            if(InlineGlyphPose!=null)InlineGlyphPose=AffineTransform.Layer(InlineLayer).Then(InlineGlyphPose);
        }
        private void UpdateInlineGeometry()
        {
            if(InlineText==null||FinishingText||mCore.JobEditingLocked)return;
            InlineMetadata.Content=InlineText.Text;
            if(string.IsNullOrWhiteSpace(InlineText.Text))
            {
                if(InlineItem!=null)InlineItem.Paths.Clear();mCore.LayerController.PreviewChanged();return;
            }
            if(!CanvasObjects.FontAvailable(InlineMetadata.FontFamily)){TextError.Text=LayerText.Get("ChooseMissingFont");return;}
            try
            {
                VectorItem generated;
                if(InlineReference==null)
                {
                    generated=CanvasObjects.Text(InlineMetadata,PointF.Empty);var b=CanvasObjects.Bounds(generated.Paths);
                    foreach(var path in generated.Paths)AffineTransform.Path(path,AffineTransform.Translation(InlineAnchor.X,InlineAnchor.Y-b.Height));
                    InlineLayer=mCore.LayerController.InsertObject(generated,InlineLayer,false);InlineItem=generated;InlineReference=generated.Clone();
                }
                else
                {
                    generated=CanvasObjects.Text(InlineMetadata,PointF.Empty,InlineReference);
                    generated.Id=InlineItem.Id;InlineLayer.VectorItems[InlineLayer.VectorItems.IndexOf(InlineItem)]=generated;InlineItem=generated;
                }
                ObjectModeButton.Checked=true;SelectedObjects.Clear();foreach(var path in InlineItem.Paths)SelectedObjects.Add(new VectorSelection(InlineLayer,InlineItem,path));
                TextError.Text="";PrepareInlineGlyphPose();RefreshSelectionEditor();mCore.LayerController.PreviewChanged();
            }
            catch(ArgumentException){TextError.Text=LayerText.Get("InvalidValue");}
        }
        private void FinishInlineText(bool accept)
        {
            if(InlineText==null||FinishingText)return;
            // An invalid font/size choice must not silently replace saved contours.
            if(accept&&TextError.Text.Length>0&&!string.IsNullOrWhiteSpace(InlineText.Text)){InlineText.Focus();return;}
            FinishingText=true;
            try
            {
                var input=InlineText;InlineText=null;CaretTimer.Stop();CaretTimer.Dispose();CaretTimer=null;
                InlinePointerAnchor=-1;(GrblPanel as Control).Capture=false;
                bool empty=string.IsNullOrWhiteSpace(input.Text);input.Dispose();
                if(accept && !(InlineWasNew&&empty))
                {
                    if(empty&&InlineItem!=null){InlineLayer.VectorItems.Remove(InlineItem);if(!InlineLayer.HasContent)InlineLayer.OutputEnabled=false;}
                    mCore.LayerController.Changed();
                }
                else mCore.LayerController.CancelEdit();
                InlineItem=InlineReference=null;InlineMetadata=null;InlineGlyphPose=null;RefreshSelectionEditor();(GrblPanel as Control).Focus();
            }
            finally{FinishingText=false;}
        }
        private bool BeginInlinePointer(MouseEventArgs e)
        {
            int hit=InlineHit(e.Location,false);if(hit<0)return false;
            InlinePointerAnchor=(ModifierKeys&Keys.Shift)!=0?InlineText.SelectionStart:hit;
            InlineText.Select(Math.Min(hit,InlinePointerAnchor),Math.Abs(hit-InlinePointerAnchor));InlineText.Focus();
            (GrblPanel as Control).Capture=true;(GrblPanel as Control).Invalidate();return true;
        }
        private bool MoveInlinePointer(MouseEventArgs e)
        {
            if(InlineText==null||InlinePointerAnchor<0)return false;
            int hit=InlineHit(e.Location,true);if(hit>=0)InlineText.Select(Math.Min(hit,InlinePointerAnchor),Math.Abs(hit-InlinePointerAnchor));
            (GrblPanel as Control).Invalidate();return true;
        }
        private int InlineHit(Point point,bool clamp)
        {
            if(InlineText==null||InlineGlyphPose==null)return -1;
            var q=InlineGlyphPose.Inverse().Apply(GrblPanel.ClientToMachine(point));
            double x=q.X+InlineItem.Text.ReferenceOffsetX,y=-q.Y-InlineItem.Text.ReferenceOffsetY;
            using(var g=(GrblPanel as Control).CreateGraphics())
            using(var family=new FontFamily(InlineMetadata.FontFamily))
            using(var font=new Font(family,(float)InlineMetadata.SizeMm,InlineMetadata.Style,GraphicsUnit.Pixel))
            using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces;
                double height=InlineMetadata.SizeMm*family.GetLineSpacing(InlineMetadata.Style)/family.GetEmHeight(InlineMetadata.Style);
                var lines=InlineText.Lines;if(lines.Length==0)return 0;
                if(!clamp&&(y<0||y>height*lines.Length))return -1;
                int row=Math.Max(0,Math.Min(lines.Length-1,(int)Math.Floor(y/height)));string line=lines[row];
                Func<int,float> width=n=>n==0?0:g.MeasureString(line.Substring(0,n),font,PointF.Empty,format).Width;
                float full=width(line.Length);x+=InlineMetadata.Alignment==StringAlignment.Center?full/2:InlineMetadata.Alignment==StringAlignment.Far?full:0;
                if(!clamp&&(x<-.5||x>full+.5))return -1;
                int column=0;while(column<line.Length&&x>(width(column)+width(column+1))/2)column++;
                return InlineText.GetFirstCharIndexFromLine(row)+column;
            }
        }
        private void DrawInlineText(Graphics g)
        {
            if(InlineText==null)return;
            if(InlineGlyphPose==null)
            {
                var p=GrblPanel.MachineToClient(InlineAnchor);using(var pen=new Pen(ColorScheme.FormForeColor,1.5f))g.DrawLine(pen,p.X,p.Y,p.X,p.Y+20);return;
            }
            // Measure in glyph coordinates; transform caret and selection just like outlines.
            using(var family=new FontFamily(InlineMetadata.FontFamily))
            using(var font=new Font(family,(float)InlineMetadata.SizeMm,InlineMetadata.Style,GraphicsUnit.Pixel))
            using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces;
                string text=InlineText.Text.Replace("\r","");var lines=text.Split('\n');
                int caret=InlineText.SelectionStart-Math.Min(InlineText.SelectionStart,InlineText.Text.Take(InlineText.SelectionStart).Count(c=>c=='\r'));
                int end=caret+InlineText.SelectedText.Replace("\r","").Length,offset=0;
                float lineHeight=(float)(InlineMetadata.SizeMm*family.GetLineSpacing(InlineMetadata.Style)/family.GetEmHeight(InlineMetadata.Style));
                // GDI glyph baseline uses raw AddString origin; retain its offset from normalized contours.
                float rawX=(float)InlineItem.Text.ReferenceOffsetX,rawY=(float)InlineItem.Text.ReferenceOffsetY;
                Func<float,float,PointF> project=(x,y)=>GrblPanel.MachineToClient(InlineGlyphPose.Apply(new PointF(x-rawX,-y-rawY)));
                using(var pen=new Pen(ColorScheme.FormForeColor,1.5f))using(var selection=new SolidBrush(Color.FromArgb(75,ColorScheme.BrandAmber)))
                for(int i=0;i<lines.Length;i++)
                {
                    var line=lines[i];int from=Math.Max(0,Math.Min(line.Length,caret-offset)),to=Math.Max(0,Math.Min(line.Length,end-offset));
                    Func<int,float> width=n=>n==0?0:g.MeasureString(line.Substring(0,n),font,PointF.Empty,format).Width;
                    float align=InlineMetadata.Alignment==StringAlignment.Center?-width(line.Length)/2:InlineMetadata.Alignment==StringAlignment.Far?-width(line.Length):0;
                    if(to>from){var p=new[]{project(align+width(from),i*lineHeight),project(align+width(to),i*lineHeight),project(align+width(to),(i+1)*lineHeight),project(align+width(from),(i+1)*lineHeight)};g.FillPolygon(selection,p);}
                    if(caret>=offset&&caret<=offset+line.Length&&InlineText.SelectionLength==0)g.DrawLine(pen,project(align+width(from),i*lineHeight),project(align+width(from),(i+1)*lineHeight));
                    offset+=line.Length+1;
                }
            }
        }
    }
}
