param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\LaserGRBL.Tests\TestResults\canvas'))
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
using System.Windows.Forms;
using LaserGRBL;
using LaserGRBL.Layering;

public static class CanvasNativeChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); checks++; }
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string svg = Path.Combine(output, "nested-groups.svg");
        string project = Path.Combine(output, "canvas-v3.lps");
        File.WriteAllText(svg, "<svg xmlns='http://www.w3.org/2000/svg' width='100mm' height='100mm' viewBox='0 0 100 100'><g id='outer' transform='translate(5 4)'><g id='inner'><rect width='10' height='12' fill='red'/></g><circle cx='30' cy='30' r='5' fill='blue'/></g></svg>");
        using (var sync = new Control())
        {
            var core = new GrblCore(sync, null, null); var controller = core.LayerController;
            try
            {
                var result = controller.ImportSvg(svg, false);
                Check(result.ImportedElements == 2 && result.SkippedElements == 0, "Native SVG parsing/import failed.");
                Check(controller.Document.Groups.Count == 2 && controller.Document.Layers.Count == 2, "Nested groups/colors lost.");
                Check(!controller.CanUndo && controller.Document.IsDirty, "Replacing import must reset history and require saving.");
                var target = controller.Document.Layers.Single(l => l.Color.R == 255);
                var source = controller.Document.Layers.Single(l => l.Color.B == 255);
                target.SpeedMmPerMinute = 777; target.PowerPercent = 22; target.Passes = 2;
                source.ScaleX = 2; source.ScaleY = 3; source.TranslateX = 7; target.TranslateY = 10;
                controller.Changed(false);
                var old = LayerGeometry.Transform(source.VectorItems[0].Paths[0], source);
                Guid pathId = source.VectorItems[0].Paths[0].Id;
                controller.SetColor(source, target.Color);
                Check(controller.Document.Layers.Count == 1, "Color collision did not merge.");
                var merged = target.VectorItems.SelectMany(i => i.Paths).Single(p => p.Id == pathId);
                var current = LayerGeometry.Transform(merged, target);
                Check(old.Points.Zip(current.Points, (a, b) => Math.Abs(a.X-b.X) < 1e-8 && Math.Abs(a.Y-b.Y) < 1e-8).All(v => v), "Merge moved geometry.");
                controller.Undo(); Check(controller.Document.Layers.Count == 2, "Merge undo failed.");
                controller.Redo(); Check(controller.Document.Layers.Count == 1, "Merge redo failed.");
                target = controller.Document.Layers[0];
                var compilation = LayeredJobCompiler.Compile(controller.Document, 1000, "M4", "M5");
                Check(!compilation.HasErrors && compilation.OutputCommands.Any(c => c.Command == "F777") && compilation.OutputCommands.Any(c => c.Command == "M4 S220"), "Destination F/S settings not compiled.");
                File.WriteAllLines(Path.Combine(output, "merged-audit.gcode"), compilation.OutputCommands.Select(c => c.Command).ToArray());
                var text = CanvasObjects.Text(new TextObject { Content = "M\u00e9xico\r\nO", SizeMm = 10 }, new PointF(30, 40));
                controller.InsertObject(text, target); controller.Save(project);
                Check(!controller.Document.IsDirty, "Save did not clear dirty state.");
                target.SpeedMmPerMinute = 900; controller.Changed(false); controller.Undo();
                Check(!controller.Document.IsDirty && controller.Document.Layers[0].SpeedMmPerMinute == 777, "Undo did not return to saved state.");
                controller.Load(project);
                Check(controller.Document.Version == 4 && controller.Document.Layers[0].VectorItems.Any(i => i.Text != null && i.Text.Content.Contains("M\u00e9xico")), "Version 4 text round trip failed.");
                Check(!controller.CanUndo && !controller.CanRedo, "Load did not clear history.");
                int before = controller.Document.Layers.Sum(l => l.VectorItems.Count);
                controller.ImportSvg(svg, true); controller.Undo();
                Check(controller.Document.Layers.Sum(l => l.VectorItems.Count) == before, "Append import must undo in one operation.");
                // The legacy GDI control uses Thread.Abort and must run under .NET Framework.
                using (var panel = new LaserGRBL.UserControls.GrblPanel())
                {
                    panel.Size = new Size(640, 440); var point = new PointF(25, 30);
                    Check(panel.MachineToClient(point) == point, "Legacy empty-canvas coordinates changed.");
                    panel.SetLayerEditing(true); var client = panel.MachineToClient(point);
                    Check(Math.Abs(client.X - 120) < .001 && Math.Abs(client.Y - 300) < .001, "Empty editor viewport is not metric.");
                    var machine = panel.ClientToMachine(client);
                    Check(Math.Abs(machine.X - point.X) < .001 && Math.Abs(machine.Y - point.Y) < .001, "Empty editor coordinate round trip failed.");
                }
                string report = checks + " native .NET Framework checks passed; no device connected or commands sent.";
                File.WriteAllText(Path.Combine(output, "native-checks.txt"), report); Console.WriteLine(report);
            }
            finally { controller.ExitLayeredMode(); }
        }
    }
}
'@
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputPath) | Out-Null
$sourcePath = Join-Path $outputPath 'CanvasNativeChecks.cs'
$libraryPath = Join-Path $outputPath 'CanvasNativeChecks.dll'
[IO.File]::WriteAllText($sourcePath, $source)
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compilerPath /nologo /target:library "/out:$libraryPath" "/reference:$applicationPath" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Canvas checks compilation failed.' }
[Reflection.Assembly]::LoadFrom($libraryPath) | Out-Null
[CanvasNativeChecks]::Run($outputPath)
