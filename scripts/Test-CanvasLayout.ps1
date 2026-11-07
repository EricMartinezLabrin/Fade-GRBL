param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\LaserGRBL.Tests\TestResults\canvas-layout'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Run with Windows PowerShell 5.1 (.NET Framework).' }
$applicationPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\LaserGRBL\bin\Release\LaserGRBL.exe')).Path
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
[Reflection.Assembly]::LoadFrom($applicationPath) | Out-Null
$source = @'
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LaserGRBL;
using LaserGRBL.Layering;
using LaserGRBL.UserControls;

public sealed class CanvasLayoutTestWindow : MainForm
{
    public CanvasLayoutTestWindow()
    {
        // PowerShell has no entry assembly; disable only unrelated instance discovery.
        ((System.Windows.Forms.Timer)typeof(MainForm).GetField("MultipleInstanceTimer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(this)).Stop();
    }
    protected override void OnLoad(EventArgs e) { }
    protected override void OnFormClosing(FormClosingEventArgs e) { }
}
public static class CanvasLayoutNativeChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static int checks;
    private static T Field<T>(object obj, string name) { return (T)obj.GetType().GetField(name, Private).GetValue(obj); }
    private static object Call(object obj, string method, params object[] args) { return obj.GetType().GetMethod(method, Private).Invoke(obj,args); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
    private static Point Pixel(IGrblPanel panel, float x, float y) { return Point.Round(panel.MachineToClient(new PointF(x,y))); }
    private static MouseEventArgs Mouse(MouseButtons button, Point p) { return new MouseEventArgs(button,1,p.X,p.Y,0); }
    private static void Drag(PreviewForm preview, Control canvas, Point start, Point end)
    {
        Call(preview,"CanvasMouseDown",canvas,Mouse(MouseButtons.Left,start));
        Call(preview,"CanvasMouseMove",canvas,Mouse(MouseButtons.Left,end));
        Call(preview,"CanvasMouseUp",canvas,Mouse(MouseButtons.Left,end));
    }
    private static void Click(PreviewForm preview, Control canvas, Point p)
    { Call(preview,"CanvasMouseDown",canvas,Mouse(MouseButtons.Left,p)); Call(preview,"CanvasMouseUp",canvas,Mouse(MouseButtons.Left,p)); }
    private static void WaitForRender(GrblPanel canvas)
    {
        for (int i=0;i<100;i++)
        {
            Application.DoEvents();
            if (Field<System.Drawing.Drawing2D.Matrix>(canvas,"mLastMatrix") != null) return;
            Thread.Sleep(10);
        }
        throw new InvalidOperationException("GDI renderer did not finish.");
    }
    public static void Run(string output)
    {
        Directory.CreateDirectory(output); Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException); Application.EnableVisualStyles();
        Console.WriteLine("Initializing native canvas controls.");
        Settings.ForcedGraphicMode = Settings.GraphicMode.GDI;
        Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("es-MX");
        typeof(MainForm).Assembly.GetType("LaserGRBL.Program").GetProperty("CurrentVersion").SetValue(null,typeof(MainForm).Assembly.GetName().Version,null);
        LaserGRBL.Icons.IconsMgr.Initialize();
        typeof(CustomButtons).GetField("buttons",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,new System.Collections.Generic.List<CustomButton>());
        try
        {
            using (var form = new CanvasLayoutTestWindow())
            {
                Console.WriteLine("Window constructed.");
                form.ClientSize = new Size(1280,800);
                var preview = (PreviewForm)typeof(MainForm).GetField("PreviewForm",Private).GetValue(form);
                var core = (GrblCore)typeof(MainForm).GetField("Core",Private).GetValue(form); var controller = core.LayerController;
                var manager = Field<LayerManagerControl>(preview,"LayerManager"); var canvas = (GrblPanel)preview.GrblPanel;
                controller.NewDocument();
                var a = controller.InsertObject(CanvasObjects.Shape(CanvasShape.Rectangle,new PointF(10,10),new PointF(30,30)),null);
                var b = controller.InsertObject(CanvasObjects.Shape(CanvasShape.Rectangle,new PointF(50,50),new PointF(70,70)),null);
                controller.RecordCellEdit(); // Cancel asynchronous compile; this harness applies a known snapshot.
                typeof(GrblCore).GetMethod("ApplyLayeredCompilation",Private).Invoke(core,new object[] { LayeredJobCompiler.Compile(controller.Document,1000,"M4","M5"),true });
                Console.WriteLine("Showing test surface.");
                form.Show(); canvas.Focus(); WaitForRender(canvas);
                Console.WriteLine("Beginning gesture checks.");
                string geometry = CanvasHistory.Key(controller.Document);
                Drag(preview,canvas,Pixel(canvas,5,75),Pixel(canvas,40,5));
                Check(manager.SelectedLayers().Count == 1 && manager.SelectedLayers()[0] == a,"Downward enclosure selection failed.");
                Drag(preview,canvas,Pixel(canvas,20,5),Pixel(canvas,5,40));
                Check(manager.SelectedLayers().Count == 1 && manager.SelectedLayers()[0] == a,"Upward crossing must select a partially touched layer.");
                Drag(preview,canvas,Pixel(canvas,5,40),Pixel(canvas,20,5));
                Check(manager.SelectedLayers().Count == 0,"Downward selection must exclude partial coverage.");
                Check(geometry == CanvasHistory.Key(controller.Document),"Selecting changed document geometry or settings.");
                manager.SelectLayers(controller.Document.Layers);
                Call(preview,"CanvasMouseDown",canvas,Mouse(MouseButtons.Left,Pixel(canvas,5,75)));
                typeof(PreviewForm).GetField("PointerAdd",Private).SetValue(preview,true);
                Call(preview,"CanvasMouseMove",canvas,Mouse(MouseButtons.Left,Pixel(canvas,40,5)));
                Call(preview,"CanvasMouseUp",canvas,Mouse(MouseButtons.Left,Pixel(canvas,40,5)));
                Check(manager.SelectedLayers().Count == 2,"Additive marquee removed existing selection.");
                Call(preview,"CanvasMouseDown",canvas,Mouse(MouseButtons.Left,Pixel(canvas,5,75)));
                Call(preview,"CanvasMouseMove",canvas,Mouse(MouseButtons.Left,Pixel(canvas,40,5)));
                Check((bool)Call(preview,"CancelSelectionPointer"),"Escape/capture cancellation was not handled.");
                Check(manager.SelectedLayers().Count == 2,"Cancel did not restore previous selection.");
                Click(preview,canvas,Pixel(canvas,10,20));
                Check(manager.SelectedLayers().Count == 2 && controller.Selection.ReferenceId == a.Id,"Click on selected layer must retain selection and change reference.");
                var originalB = b.TranslateX; controller.Align(CanvasAlignmentAction.Right,false);
                Check(Math.Abs(b.TranslateX-originalB+40)<.001,"Reference alignment moved wrong layer or used wrong edge.");
                controller.Undo(); Check(controller.Selection.ReferenceId == a.Id && controller.Document.Layers[1].TranslateX == originalB,"Undo lost alignment or reference.");
                var text = CanvasObjects.Text(new TextObject { Content = "O", SizeMm = 5 },new PointF(12,12));
                controller.InsertObject(text,controller.Document.Layers[0]);
                var raster = new LaserLayer { ContentKind = LayerContentKind.Raster, Mode = LayerMode.Image,
                    RasterItem = new RasterItem { BaseCommands = { "G0 X40 Y10","M4 S20","G1 X45 Y15","M5" } } };
                controller.Document.Layers.Add(raster); controller.Changed(false);
                Field<ToolStripButton>(preview,"ObjectModeButton").Checked = true;
                Drag(preview,canvas,Pixel(canvas,5,40),Pixel(canvas,35,5));
                Check(Field<System.Collections.Generic.List<VectorSelection>>(preview,"SelectedObjects").Count == 1+text.Paths.Count,"Object marquee must select text as a complete object without selecting other layers.");
                Check(controller.Selection.Objects,"Object marquee changed selection mode.");
                Drag(preview,canvas,Pixel(canvas,39,17),Pixel(canvas,46,9));
                Check(Field<System.Collections.Generic.List<LaserLayer>>(preview,"SelectedRasterItems").Count == 1 && controller.Selection.ReferenceId == raster.Id,"Raster object was not selectable by marquee.");
                Call(preview,"CopyCanvasSelection"); Call(preview,"PasteCanvasSelection",false,false);
                Check(controller.Document.Layers.Count(l => l.ContentKind == LayerContentKind.Raster) == 2,"Selected image could not be copied as an object.");
                controller.Undo(); Check(Field<System.Collections.Generic.List<LaserLayer>>(preview,"SelectedRasterItems").Count == 1,"Undo lost selected raster object.");
                Field<ToolStripButton>(preview,"ObjectModeButton").Checked = false;
                var position = canvas.MachineToClient(new PointF(10,20)); geometry = CanvasHistory.Key(controller.Document);
                bool undo = controller.CanUndo, redo = controller.CanRedo;
                Call(canvas,"NavigationMouseDown",canvas,Mouse(MouseButtons.Middle,new Point(100,100)));
                Call(canvas,"NavigationMouseMove",canvas,Mouse(MouseButtons.Middle,new Point(140,115)));
                Call(canvas,"EndPan");
                var moved = canvas.MachineToClient(new PointF(10,20));
                Check(Math.Abs(moved.X-position.X-40)<.01 && Math.Abs(moved.Y-position.Y-15)<.01,"Middle-button pan did not update viewport coordinates.");
                var inverse = canvas.ClientToMachine(moved);
                Check(Math.Abs(inverse.X-10)<.001 && Math.Abs(inverse.Y-20)<.001,"Pan broke inverse coordinate mapping.");
                Check(geometry == CanvasHistory.Key(controller.Document) && undo == controller.CanUndo && redo == controller.CanRedo,"Pan changed document or undo history.");
                Check(!canvas.Capture,"Middle-button release kept mouse capture.");
                controller.RecordCellEdit();
                typeof(GrblCore).GetMethod("ApplyLayeredCompilation",Private).Invoke(core,new object[] { LayeredJobCompiler.Compile(controller.Document,1000,"M4","M5"),true });
                Application.DoEvents(); Thread.Sleep(80); Application.DoEvents();
                Check(canvas.MachineToClient(new PointF(10,20)) == moved,"Compilation reset panned viewport.");
                Call(canvas,"NavigationMouseDown",canvas,Mouse(MouseButtons.Middle,new Point(100,100)));
                object[] key = { new Message(),Keys.Escape }; Call(canvas,"ProcessCmdKey",key);
                Check(!canvas.Capture,"Escape did not release pan capture.");
                controller.NewDocument();
                var view = Field<CanvasViewport>(canvas,"viewport");
                using (var snapshot = view.Snapshot()) Check(snapshot == null,"New document did not reset viewport.");
                Check(controller.Selection.ReferenceId == Guid.Empty,"New document retained reference.");
                var fractional = controller.InsertObject(CanvasObjects.Shape(CanvasShape.Rectangle,new PointF(10,10),new PointF(30.123457f,30.765432f)),null);
                manager.SelectLayers(new[] { fractional }); controller.History.Reset(controller.Document);
                string untouched = CanvasHistory.Key(controller.Document);
                for (int i=0;i<3;i++) { Call(preview,"ApplyTypedSize",true); Call(preview,"ApplyTypedSize",false); }
                Check(!controller.CanUndo && untouched == CanvasHistory.Key(controller.Document),"Untouched rounded size fields created invisible undo steps.");
                controller.RecordCellEdit();
                typeof(GrblCore).GetMethod("ApplyLayeredCompilation",Private).Invoke(core,new object[] { LayeredJobCompiler.Compile(controller.Document,1000,"M4","M5"),true });
                Call(canvas,"AbortCreation"); Call(canvas,"DoTheWork");
                canvas.Focus(); Check(canvas.ContainsFocus,"Canvas did not receive shortcut focus.");
                string beforeDrag = CanvasHistory.Key(controller.Document);
                RectangleF fractionalBounds; LayerGeometry.TryGetBounds(controller.Document.Layers[0],out fractionalBounds);
                Point dragStart = Pixel(canvas,fractionalBounds.Right,fractionalBounds.Top+fractionalBounds.Height/2);
                Call(preview,"CanvasMouseDown",canvas,Mouse(MouseButtons.Left,dragStart));
                for (int i=1;i<=20;i++)
                {
                    var p = Pixel(canvas,fractionalBounds.Right+i*.5f,fractionalBounds.Top+fractionalBounds.Height/2);
                    Call(preview,"CanvasMouseMove",canvas,Mouse(MouseButtons.Left,p));
                }
                Call(preview,"CanvasMouseUp",canvas,Mouse(MouseButtons.Left,Pixel(canvas,fractionalBounds.Right+10,fractionalBounds.Top+fractionalBounds.Height/2)));
                string afterDrag = CanvasHistory.Key(controller.Document);
                Check(beforeDrag != afterDrag,"Drag did not change geometry.");
                object[] undoKey = { new Message(),Keys.Control|Keys.Z };
                Check((bool)Call(canvas,"ProcessCmdKey",undoKey),"Canvas did not handle Ctrl+Z.");
                Check(beforeDrag == CanvasHistory.Key(controller.Document) && !controller.CanUndo,"One drag required more than one Ctrl+Z.");
                object[] redoKey = { new Message(),Keys.Control|Keys.Shift|Keys.Z };
                Call(canvas,"ProcessCmdKey",redoKey);
                Check(afterDrag == CanvasHistory.Key(controller.Document),"One Ctrl+Shift+Z did not restore drag.");
                controller.History.Reset(controller.Document);
                string beforeResize = CanvasHistory.Key(controller.Document);
                LayerGeometry.TryGetBounds(controller.Document.Layers[0],out fractionalBounds);
                var handle = Field<Panel>(preview,"ResizeHandle");
                Call(preview,"ResizeHandleMouseDown",handle,Mouse(MouseButtons.Left,new Point(7,7)));
                for (int i=1;i<=12;i++)
                {
                    float ratio = 1+i/24f;
                    var target = Pixel(canvas,fractionalBounds.Left+fractionalBounds.Width*ratio,fractionalBounds.Bottom-fractionalBounds.Height*ratio);
                    var local = handle.PointToClient(canvas.PointToScreen(target));
                    Call(preview,"ResizeHandleMouseMove",handle,Mouse(MouseButtons.Left,local));
                }
                Call(preview,"ResizeHandleMouseUp",handle,Mouse(MouseButtons.Left,new Point(7,7)));
                string afterResize = CanvasHistory.Key(controller.Document);
                Check(afterResize != beforeResize,"Resize gesture did not change geometry.");
                Call(canvas,"ProcessCmdKey",undoKey);
                Check(beforeResize == CanvasHistory.Key(controller.Document) && !controller.CanUndo,"One resize required more than one Ctrl+Z.");
                Call(canvas,"ProcessCmdKey",redoKey);
                Check(afterResize == CanvasHistory.Key(controller.Document),"Redo did not restore resize in one step.");
                controller.History.Reset(controller.Document);
                string beforeTyping = CanvasHistory.Key(controller.Document);
                var widthBox = Field<ToolStripTextBox>(preview,"WidthBox");
                widthBox.TextBox.Focus(); widthBox.Text = "50"; Call(preview,"ApplyTypedSize",true);
                Check(controller.CanUndo && beforeTyping != CanvasHistory.Key(controller.Document),"Explicit numeric edit was not recorded.");
                Field<ToolStripButton>(preview,"UndoButton").PerformClick();
                Check(beforeTyping == CanvasHistory.Key(controller.Document),"Undo button did not restore typed size while field retained focus.");
                canvas.Focus(); Call(preview,"ApplyTypedSize",true); Call(preview,"ApplyTypedSize",false);
                Check(beforeTyping == CanvasHistory.Key(controller.Document) && !controller.CanUndo && controller.CanRedo,"Leaving field after Undo reapplied stale size.");
                Field<ToolStripButton>(preview,"ObjectModeButton").Checked = true;
                var objectLayer = controller.Document.Layers[0]; var objectItem = objectLayer.VectorItems[0];
                Field<System.Collections.Generic.List<VectorSelection>>(preview,"SelectedObjects").Add(new VectorSelection(objectLayer,objectItem,objectItem.Paths[0]));
                Call(preview,"RefreshSelectionEditor"); controller.History.Reset(controller.Document);
                string beforeObject = CanvasHistory.Key(controller.Document);
                Call(preview,"ApplyTypedSize",true); Call(preview,"ApplyTypedSize",false);
                Check(!controller.CanUndo && beforeObject == CanvasHistory.Key(controller.Document),"Untouched object measures created undo entries.");
                var objectBounds = new VectorSelection(objectLayer,objectItem,objectItem.Paths[0]).Bounds;
                Drag(preview,canvas,Pixel(canvas,objectBounds.Right,objectBounds.Top+objectBounds.Height/2),Pixel(canvas,objectBounds.Right+5,objectBounds.Top+objectBounds.Height/2));
                string movedObject = CanvasHistory.Key(controller.Document);
                Check(movedObject != beforeObject,"Object drag did not change geometry.");
                Call(canvas,"ProcessCmdKey",undoKey);
                Check(beforeObject == CanvasHistory.Key(controller.Document) && !controller.CanUndo,"Object movement required multiple undo steps.");
                Call(canvas,"ProcessCmdKey",redoKey); Check(movedObject == CanvasHistory.Key(controller.Document),"Object movement redo failed.");
                controller.History.Reset(controller.Document); string beforeObjectResize = CanvasHistory.Key(controller.Document);
                objectLayer = controller.Document.Layers[0]; objectItem = objectLayer.VectorItems[0]; objectBounds = new VectorSelection(objectLayer,objectItem,objectItem.Paths[0]).Bounds;
                Call(preview,"ResizeHandleMouseDown",handle,Mouse(MouseButtons.Left,new Point(7,7)));
                var objectTarget = Pixel(canvas,objectBounds.Right+5,objectBounds.Top-5);
                Call(preview,"ResizeHandleMouseMove",handle,Mouse(MouseButtons.Left,handle.PointToClient(canvas.PointToScreen(objectTarget))));
                Call(preview,"ResizeHandleMouseUp",handle,Mouse(MouseButtons.Left,new Point(7,7)));
                string resizedObject = CanvasHistory.Key(controller.Document);
                Check(resizedObject != beforeObjectResize,"Object resize did not change geometry.");
                Call(canvas,"ProcessCmdKey",undoKey);
                Check(beforeObjectResize == CanvasHistory.Key(controller.Document) && !controller.CanUndo,"Object resizing required multiple undo steps.");
                Call(canvas,"ProcessCmdKey",redoKey); Check(resizedObject == CanvasHistory.Key(controller.Document),"Object resize redo failed.");

                controller.NewDocument(); controller.History.Reset(controller.Document);
                Call(preview,"SelectCanvasTool",CanvasShape.Text);
                Click(preview,canvas,new Point(canvas.Width/3,canvas.Height/3));
                var input=Field<RichTextBox>(preview,"InlineText");
                Check(input!=null && input.Focused,"Text tool did not begin inline native input.");
                var watch=System.Diagnostics.Stopwatch.StartNew();input.Text="M\u00e9xico";watch.Stop();
                Check(watch.ElapsedMilliseconds<100,"Simple inline text took >= 100ms: "+watch.ElapsedMilliseconds);
                Check(!controller.IsCompilationCurrent && !controller.CanUndo,"Typing compiled or committed history before confirmation.");
                Check(controller.Document.Layers.SelectMany(l=>l.VectorItems).Any(v=>v.Text!=null&&v.Text.Content==input.Text),"Inline typing did not immediately update document outlines.");
                bool painted=false;PaintEventHandler witness=(sender,args)=>painted=true;canvas.Paint+=witness;watch.Restart();input.AppendText("\r\nO");
                using(var rendered=new Bitmap(canvas.Width,canvas.Height)) {canvas.DrawToBitmap(rendered,canvas.ClientRectangle);rendered.Save(Path.Combine(output,"inline-text.png"));}
                watch.Stop();canvas.Paint-=witness;Check(painted,"Native canvas did not emit Paint: caret/handles would be invisible.");
                Check(watch.ElapsedMilliseconds<100,"Inline text and scene rendering took >=100ms: "+watch.ElapsedMilliseconds);
                string beforeTextUndo=input.Text;input.Select(input.TextLength,0);input.SelectedText=" extra";
                Check(input.CanUndo&&CanvasKeyboard.IsTextEditing(form),"Inline input did not own text undo/keyboard.");
                input.Undo();Check(input.Text==beforeTextUndo,"Native inline text undo failed.");
                object[] textRedoKey={new Message(),Keys.Control|Keys.Shift|Keys.Z};Call(preview,"ProcessCmdKey",textRedoKey);
                Check(input.Text==beforeTextUndo+" extra","Ctrl+Shift+Z did not redo inline text.");
                input.Undo();Check(!controller.CanUndo,"Text undo polluted document history before confirmation.");
                Call(preview,"FinishInlineText",true);
                Check(controller.CanUndo,"Inline session was not committed.");
                var typed=CanvasHistory.Key(controller.Document);controller.Undo();
                Check(!controller.Document.Layers.Any(l=>l.HasContent)&&!controller.CanUndo,"Inline session required more than one undo.");
                controller.Redo();Check(typed==CanvasHistory.Key(controller.Document),"Inline redo lost text.");
                var textLayer=controller.Document.Layers[0];var textItem=textLayer.VectorItems[0];
                var textSelection=new VectorSelection(textLayer,textItem,textItem.Paths[0]);
                Call(preview,"BeginInlineText",textSelection,PointF.Empty);
                input=Field<RichTextBox>(preview,"InlineText");input.Text="cancelled";Call(preview,"FinishInlineText",false);
                Check(typed==CanvasHistory.Key(controller.Document),"Escape did not restore inline editing.");
                textLayer=controller.Document.Layers[0];textItem=textLayer.VectorItems[0];
                Call(preview,"BeginInlineText",new VectorSelection(textLayer,textItem,textItem.Paths[0]),PointF.Empty);
                input=Field<RichTextBox>(preview,"InlineText");input.Text="";Call(preview,"FinishInlineText",true);
                Check(!controller.Document.Layers.Any(l=>l.HasContent),"Accepting empty text did not remove object.");
                controller.Undo();Check(typed==CanvasHistory.Key(controller.Document),"Deleting text could not be undone.");
                string beforeEmpty=CanvasHistory.Key(controller.Document);Call(preview,"BeginInlineText",null,new PointF(65,60));
                input=Field<RichTextBox>(preview,"InlineText");input.Text="temporary";input.Text="";Call(preview,"FinishInlineText",true);
                Check(beforeEmpty==CanvasHistory.Key(controller.Document),"New text emptied before confirmation created an object/history entry.");
                textLayer=controller.Document.Layers[0];textItem=textLayer.VectorItems[0];
                Field<ToolStripButton>(preview,"ObjectModeButton").Checked=true;
                var chosen=Field<System.Collections.Generic.List<VectorSelection>>(preview,"SelectedObjects");chosen.Clear();chosen.AddRange(textItem.Paths.Select(p=>new VectorSelection(textLayer,textItem,p)));
                Call(preview,"RefreshSelectionEditor"); controller.History.Reset(controller.Document);
                var angle=Field<ToolStripTextBox>(preview,"AngleBox");angle.Text="45";Call(preview,"ApplyTransformField",angle);
                Check(Math.Abs((double)Call(preview,"SelectionAngle")-45)<.001,"Absolute numeric rotation failed.");
                var rotated=CanvasHistory.Key(controller.Document);
                Call(preview,"BeginInlineText",chosen[0],PointF.Empty);input=Field<RichTextBox>(preview,"InlineText");input.AppendText(" largo");Call(preview,"FinishInlineText",true);
                Check(Math.Abs((double)Call(preview,"SelectionAngle")-45)<.001,"Editing rotated text lost orientation.");
                controller.Undo();Check(rotated==CanvasHistory.Key(controller.Document),"Undo of transformed text edit failed.");
                var shear=Field<ToolStripTextBox>(preview,"ShearXBox");shear.Text="20";Call(preview,"ApplyTransformField",shear);
                Check(CanvasHistory.Key(controller.Document)!=rotated,"Numeric inclination did not transform text.");
                controller.Undo();Check(rotated==CanvasHistory.Key(controller.Document),"Inclination undo failed.");

                // Exercise the actual transform pointer handlers, not only the geometry service.
                var points=(System.Collections.Generic.Dictionary<string,PointF>)Call(preview,"TransformHandles");
                Point rotate=Point.Round(points["rotate"]);string beforeGesture=CanvasHistory.Key(controller.Document);controller.History.Reset(controller.Document);
                Call(preview,"CanvasMouseDown",canvas,Mouse(MouseButtons.Left,rotate));
                Call(preview,"CanvasMouseMove",canvas,Mouse(MouseButtons.Left,new Point(rotate.X+35,rotate.Y+12)));
                Check(beforeGesture!=CanvasHistory.Key(controller.Document),"Rotation handle did not update document immediately.");
                Call(preview,"CanvasMouseUp",canvas,Mouse(MouseButtons.Left,new Point(rotate.X+35,rotate.Y+12)));
                Check(controller.CanUndo,"Rotation gesture was not recorded.");controller.Undo();
                Check(beforeGesture==CanvasHistory.Key(controller.Document)&&!controller.CanUndo,"Rotation gesture was not one undo step.");
                points=(System.Collections.Generic.Dictionary<string,PointF>)Call(preview,"TransformHandles");rotate=Point.Round(points["shearX"]);
                Call(preview,"CanvasMouseDown",canvas,Mouse(MouseButtons.Left,rotate));Call(preview,"CanvasMouseMove",canvas,Mouse(MouseButtons.Left,new Point(rotate.X+25,rotate.Y)));
                Check(beforeGesture!=CanvasHistory.Key(controller.Document),"Inclination handle did not change geometry.");
                Call(preview,"CancelAffineGesture");Check(beforeGesture==CanvasHistory.Key(controller.Document)&&!controller.CanUndo,"Canceled inclination changed geometry/history.");
                points=(System.Collections.Generic.Dictionary<string,PointF>)Call(preview,"TransformHandles");rotate=Point.Round(points["pivot"]);
                Call(preview,"CanvasMouseDown",canvas,Mouse(MouseButtons.Left,rotate));Call(preview,"CanvasMouseMove",canvas,Mouse(MouseButtons.Left,new Point(rotate.X+10,rotate.Y+15)));Call(preview,"CanvasMouseUp",canvas,Mouse(MouseButtons.Left,new Point(rotate.X+10,rotate.Y+15)));
                Check(beforeGesture==CanvasHistory.Key(controller.Document)&&!controller.CanUndo,"Moving pivot altered geometry/history.");
                controller.NewDocument();
                var lineLayer=controller.InsertObject(CanvasObjects.Shape(CanvasShape.Line,new PointF(10,10),new PointF(30,10)),null);var lineItem=lineLayer.VectorItems[0];
                manager.SelectLayers(new[]{lineLayer});Field<ToolStripButton>(preview,"ObjectModeButton").Checked=true;
                chosen.Clear();chosen.Add(new VectorSelection(lineLayer,lineItem,lineItem.Paths[0]));Call(preview,"RefreshSelectionEditor");
                angle.Text="90";Call(preview,"ApplyTransformField",angle);
                Check(Math.Abs((double)Call(preview,"SelectionAngle")-90)<.001,"Horizontal line could not be rotated vertically.");
                var length=Field<ToolStripTextBox>(preview,"LengthBox");length.Text="35";Call(preview,"ApplyTransformField",length);
                var worldLine=LayerGeometry.Transform(lineItem.Paths[0],lineLayer).Points;
                Check(Math.Abs(Math.Sqrt(Math.Pow(worldLine[1].X-worldLine[0].X,2)+Math.Pow(worldLine[1].Y-worldLine[0].Y,2))-35)<.001,"Vertical line length field failed.");
                controller.History.Reset(controller.Document);string lineBefore=CanvasHistory.Key(controller.Document);
                points=(System.Collections.Generic.Dictionary<string,PointF>)Call(preview,"TransformHandles");rotate=Point.Round(points["end"]);
                Drag(preview,canvas,rotate,new Point(rotate.X+20,rotate.Y+10));
                Check(lineBefore!=CanvasHistory.Key(controller.Document),"Line endpoint handle did not move.");controller.Undo();
                Check(lineBefore==CanvasHistory.Key(controller.Document)&&!controller.CanUndo,"Endpoint movement was not one undo step.");
                Field<ToolStripButton>(preview,"ObjectModeButton").Checked=false;
                manager.SelectLayers(new[]{controller.Document.Layers[0]});Call(preview,"RefreshSelectionEditor");
                points=(System.Collections.Generic.Dictionary<string,PointF>)Call(preview,"TransformHandles");rotate=Point.Round(points["end"]);
                Drag(preview,canvas,rotate,new Point(rotate.X+15,rotate.Y-5));
                Check(lineBefore!=CanvasHistory.Key(controller.Document),"Layer-mode line endpoint did not move.");controller.Undo();
                Check(lineBefore==CanvasHistory.Key(controller.Document)&&!controller.CanUndo,"Layer-mode endpoint undo failed.");
                var bad=Field<ToolStripTextBox>(preview,"ShearYBox");bad.Text="90";Call(preview,"ApplyTransformField",bad);
                Check(bad.Text=="90"&&CanvasHistory.Key(controller.Document)==lineBefore&&!controller.CanUndo,"Invalid slant was accepted or discarded input.");
                Call(preview,"BeginInlineText",null,new PointF(20,20));Field<RichTextBox>(preview,"InlineText").Text="replace document";controller.NewDocument();
                Check(Field<RichTextBox>(preview,"InlineText")==null&&!controller.Document.Layers.Any(),"Replacing document retained transient text session.");
                string latency="Inline text + native scene DrawToBitmap: "+watch.ElapsedMilliseconds+"ms; 100ms target; no G-code compilation awaited.";
                File.WriteAllText(Path.Combine(output,"latency.txt"),latency);Console.WriteLine(latency);
                string report = checks + " canvas selection/alignment/navigation checks passed; no device connected or commands sent.";
                File.WriteAllText(Path.Combine(output,"native-checks.txt"),report); Console.WriteLine(report);
            }
        }
        finally { if (SincroStart.Running()) SincroStart.StopListen(); }
    }
}
'@
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputPath) | Out-Null
$sourcePath = Join-Path $outputPath 'CanvasLayoutNativeChecks.cs'
$libraryPath = Join-Path $outputPath 'CanvasLayoutNativeChecks.dll'
[IO.File]::WriteAllText($sourcePath, $source)
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compilerPath /nologo /target:library "/out:$libraryPath" "/reference:$applicationPath" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Canvas layout checks compilation failed.' }
[Reflection.Assembly]::LoadFrom($libraryPath) | Out-Null
try { [CanvasLayoutNativeChecks]::Run($outputPath) }
catch { Write-Output $_.Exception.ToString(); throw }
