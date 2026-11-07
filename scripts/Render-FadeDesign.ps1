param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\LaserGRBL.Tests\TestResults\design'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Run with Windows PowerShell 5.1 (.NET Framework), not pwsh.' }
$applicationPath = Join-Path $PSScriptRoot '..\LaserGRBL\bin\Release\LaserGRBL.exe'
$applicationPath = (Resolve-Path -LiteralPath $applicationPath).Path
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

public sealed class FadeSnapshotWindow : MainForm
{
    protected override void OnLoad(EventArgs e) { }
    protected override void OnFormClosing(FormClosingEventArgs e) { }
}
public static class FadeDesignSnapshots
{
    private static void SaveDialog(string typeName, object[] arguments, ColorScheme.Scheme scheme, string output, string suffix)
    {
        Type type = typeof(MainForm).Assembly.GetType("LaserGRBL.Layering." + typeName);
        using (var dialog = (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null))
        {
            FadeTheme.Apply(dialog); dialog.Show(); dialog.PerformLayout();
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
            { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(output, scheme + "-" + suffix + ".png")); }
        }
    }
    private static void Dump(Control control, int depth)
    {
        Console.WriteLine(new string(' ', depth) + control.GetType().Name + " " + control.Name + " size=" + control.Size + " preferred=" + control.GetPreferredSize(Size.Empty) + " auto=" + control.AutoSize);
        foreach (Control child in control.Controls) Dump(child, depth + 1);
    }
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        Application.EnableVisualStyles();
        Settings.ForcedGraphicMode = Settings.GraphicMode.GDI;
        Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("es-MX");
        typeof(MainForm).Assembly.GetType("LaserGRBL.Program").GetProperty("CurrentVersion").SetValue(null, typeof(MainForm).Assembly.GetName().Version, null);
        using (var splash = new SplashScreenForm())
        {
            splash.Show(); splash.PerformLayout();
            using (var bitmap = new Bitmap(splash.Width, splash.Height))
            {
                splash.DrawToBitmap(bitmap, new Rectangle(Point.Empty, splash.Size));
                bitmap.Save(Path.Combine(output, "FadeSplash.png"));
            }
            splash.Close();
        }
        LaserGRBL.Icons.IconsMgr.Initialize();
        typeof(CustomButtons).GetField("buttons", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, new System.Collections.Generic.List<CustomButton>());
        foreach (ColorScheme.Scheme scheme in new[] { ColorScheme.Scheme.FadeLight, ColorScheme.Scheme.FadeDark })
        {
            Settings.SetObject("Fade Design Theme", scheme);
            try
            {
                using (var form = new FadeSnapshotWindow())
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.WindowState = FormWindowState.Normal;
                    form.ClientSize = new Size(1280, 800);
                    var shell = form.Controls.OfType<TableLayoutPanel>().Single();
                    var core = (GrblCore)typeof(MainForm).GetField("Core", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    core.LayerController.NewDocument();
                    var document = new LaserDocument();
                    for (int i = 0; i < 3; i++)
                    {
                        var layer = new LaserLayer { Name = new[] { "Corte exterior", "Cortes interiores", "Detalle" }[i], Order = i,
                            Color = new[] { Color.FromArgb(57, 62, 70), Color.FromArgb(246, 180, 42), Color.FromArgb(23, 92, 211) }[i],
                            Passes = 10, SpeedMmPerMinute = 600, PowerPercent = 75, OutputEnabled = i != 2 };
                        layer.VectorItems.Add(new VectorItem { Paths = { new VectorPath { Closed = true, Points = {
                            new LaserPoint(10 + i*15, 10 + i*15), new LaserPoint(90 - i*15, 10 + i*15),
                            new LaserPoint(90 - i*15, 90 - i*15), new LaserPoint(10 + i*15, 90 - i*15) } } } });
                        document.Layers.Add(layer);
                    }
                    typeof(LayerDocumentController).GetProperty("Document").SetValue(core.LayerController, document, null);
                    core.LayerController.Changed(false);
                    typeof(GrblCore).GetMethod("ApplyLayeredCompilation", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(core,
                        new object[] { LayeredJobCompiler.Compile(document, 1000, "M4", "M5"), true });
                    ((ConnectLogForm)typeof(MainForm).GetField("ConnectionForm", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form)).TimerUpdate();
                    ((PreviewForm)typeof(MainForm).GetField("PreviewForm", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form)).TimerUpdate();
                    form.Show(); form.PerformLayout();
                    var workspace = (SplitContainer)shell.GetControlFromPosition(0, 2);
                    if (Environment.GetEnvironmentVariable("FADE_DESIGN_DEBUG") == "1") Dump(shell.GetControlFromPosition(0, 3), 0);
                    if (workspace.Height < 180) throw new InvalidOperationException("Workspace height: " + workspace.Height);
                    foreach (Size size in new[] { new Size(1280, 800), new Size(1024, 768), new Size(1920, 1080) })
                    {
                        form.ClientSize = size; form.PerformLayout();
                        foreach (Control child in shell.Controls) child.PerformLayout();
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                            bitmap.Save(Path.Combine(output, scheme + "-" + size.Width + ".png"));
                        }
                        Console.WriteLine(scheme + " " + size + " workspace=" + workspace.Size + " progress=" + shell.GetControlFromPosition(0, 3).Size);
                    }
                    form.ClientSize = new Size(1280, 800); form.PerformLayout();
                    var preview = (PreviewForm)typeof(MainForm).GetField("PreviewForm", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    var manager = (LayerManagerControl)typeof(PreviewForm).GetField("LayerManager", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
                    manager.SelectLayers(document.Layers.Take(2).ToList());
                    var reference = (ToolStripMenuItem)typeof(PreviewForm).GetField("AlignToReference", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
                    reference.PerformClick();
                    foreach (bool crossing in new[] { false, true })
                    {
                        var selector = new RectangleF(80, 80, 220, 180);
                        var selected = CanvasLayout.Units(document, false).Where(u => CanvasLayout.Matches(u, selector, crossing, preview.GrblPanel.MachineToClient)).ToList();
                        manager.SelectLayers(selected.SelectMany(u => u.Layers).ToList());
                        preview.GrblPanel.SetSelectionRectangle(selector, crossing);
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                            bitmap.Save(Path.Combine(output, scheme + (crossing ? "-selection-up.png" : "-selection-down.png")));
                        }
                    }
                    preview.GrblPanel.SetSelectionRectangle(null, false);
                    var align = (ToolStripDropDownButton)typeof(PreviewForm).GetField("AlignButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
                    align.ShowDropDown();
                    using (var bitmap = new Bitmap(align.DropDown.Width, align.DropDown.Height))
                    { align.DropDown.DrawToBitmap(bitmap, new Rectangle(Point.Empty, align.DropDown.Size)); bitmap.Save(Path.Combine(output, scheme + "-alignment-menu.png")); }
                    align.HideDropDown();
                    SaveDialog("JobPassOrderDialog", new object[] { JobPassOrder.ByObject }, scheme, output, "pass-order");
                    SaveDialog("LayerColorPickerForm", new object[] { Color.FromArgb(246, 180, 42) }, scheme, output, "color-picker");
                    var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                    var canvas=(Control)preview.GrblPanel;
                    typeof(PreviewForm).GetMethod("BeginInlineText",flags).Invoke(preview,new object[]{null,preview.GrblPanel.ClientToMachine(new PointF(canvas.Width/5,canvas.Height/4))});
                    ((RichTextBox)typeof(PreviewForm).GetField("InlineText",flags).GetValue(preview)).Text="M\u00e9xico\r\nTexto editable";
                    form.PerformLayout();
                    using(var bitmap=new Bitmap(form.Width,form.Height))
                    {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(output,scheme+"-text-editor.png"));}
                    typeof(PreviewForm).GetMethod("FinishInlineText",flags).Invoke(preview,new object[]{true});
                    var angle=(ToolStripTextBox)typeof(PreviewForm).GetField("AngleBox",flags).GetValue(preview);
                    angle.Text="25";typeof(PreviewForm).GetMethod("ApplyTransformField",flags).Invoke(preview,new object[]{angle});
                    var shear=(ToolStripTextBox)typeof(PreviewForm).GetField("ShearXBox",flags).GetValue(preview);
                    shear.Text="12";typeof(PreviewForm).GetMethod("ApplyTransformField",flags).Invoke(preview,new object[]{shear});
                    using(var bitmap=new Bitmap(form.Width,form.Height))
                    {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(output,scheme+"-transformed-text.png"));}
                    var session = new JobExecutionSession(LayeredJobCompiler.Compile(document, 1000, "M4", "M5").OutputCommands, document, 1);
                    session.SetState(JobExecutionState.Paused);
                    SaveDialog("JobParametersDialog", new object[] { document, session }, scheme, output, "parameters");
                    SaveDialog("JobResumeDialog", new object[] { session, false, false }, scheme, output, "resume");
                }
            }
            finally { if (SincroStart.Running()) SincroStart.StopListen(); }
        }
    }
}
'@
$renderOutput = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($renderOutput) | Out-Null
$sourcePath = Join-Path $renderOutput 'Render-FadeDesign.cs'
$libraryPath = Join-Path $renderOutput 'Render-FadeDesign.dll'
[IO.File]::WriteAllText($sourcePath, $source)
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compilerPath /nologo /target:library "/out:$libraryPath" "/reference:$applicationPath" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Snapshot harness compilation failed.' }
[Reflection.Assembly]::LoadFrom($libraryPath) | Out-Null
try { [FadeDesignSnapshots]::Run($renderOutput) }
catch { Write-Output $_.Exception.ToString(); throw }
