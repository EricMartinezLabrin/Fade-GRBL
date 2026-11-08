param([string]$OutputDirectory = 'LaserGRBL.Tests/TestResults/community-ui')
$ErrorActionPreference = 'Stop'
$applicationPath = [IO.Path]::GetFullPath('LaserGRBL/bin/Release/LaserGRBL.exe')
if (!(Test-Path -LiteralPath $applicationPath)) { throw 'Build Release first.' }
$renderOutput = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($renderOutput) | Out-Null
[Reflection.Assembly]::LoadFrom($applicationPath) | Out-Null
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
# Isolate legacy Settings and timers; never change real user configuration.
$flags = [Reflection.BindingFlags]'Static,NonPublic'
[LaserGRBL.GrblCore].GetField('mDataPath',$flags).SetValue($null,(Join-Path $renderOutput 'isolated-data'))
[IO.Directory]::CreateDirectory((Join-Path $renderOutput 'isolated-data')) | Out-Null
$source = @'
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LaserGRBL;
using LaserGRBL.Community;
using LaserGRBL.Layering;
using LaserGRBL.UserControls;

public static class CommunitySnapshots
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Flags).GetValue(target); }
    private static void Save(Form form, string path)
    {
        form.PerformLayout(); Application.DoEvents();
        using (var bitmap = new Bitmap(form.Width,form.Height))
        { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size)); bitmap.Save(path); }
    }
    public static void Run(string output)
    {
        Application.EnableVisualStyles(); Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("es-MX");
        foreach(var scheme in new[]{ColorScheme.Scheme.FadeLight,ColorScheme.Scheme.FadeDark})
        {
            ColorScheme.CurrentScheme=scheme;
            using(var form=new CommunityForm(null))
            {
                form.StartPosition=FormStartPosition.Manual; form.Location=new Point(0,0); form.Show();
                var local=Field<ListBox>(form,"localList");
                local.Items.Add(new LibraryEntry { title="Caja encastrable",tags=new[]{"madera","cajas"} });
                local.Items.Add(new LibraryEntry { title="Letrero México",acquisitionId="sample-acquisition" });
                Field<ListBox>(form,"remoteList").Items.Add(new LibraryEntry { title="Caja encastrable",version=2 });
                Field<Label>(form,"quota").Text="Nube privada: 24.00 MB usados + 0.00 MB reservados / 200.00 MB.";
                var sample=new LaserDocument(); var layer=new LaserLayer(); sample.Layers.Add(layer);
                var item=new VectorItem(); layer.VectorItems.Add(item);
                var contour=new VectorPath { Closed=true }; item.Paths.Add(contour);
                contour.Points.Add(new LaserPoint(0,0));contour.Points.Add(new LaserPoint(40,0));contour.Points.Add(new LaserPoint(40,25));contour.Points.Add(new LaserPoint(0,25));
                var thumbnail=CommunitySvg.Thumbnail(sample);
                using(var stream=new MemoryStream(Convert.FromBase64String(thumbnail)))
                using(var image=Image.FromStream(stream)) Field<PictureBox>(form,"preview").Image=new Bitmap(image);
                var design=new CommunityDesign { id="sample-design",title="Caja encastrable",description="Diseño para fabricar una caja. Vista ficticia para revisión visual.",category="Cajas",tags=new[]{"madera","encastre"},designer=new CommunityUser { id="sample-designer",displayName="Diseños Ana" },licenseVersion="physical-products-v1",thumbnail=thumbnail };
                var market=Field<ListBox>(form,"marketList");market.Items.Add(design);market.SelectedIndex=0;
                form.GetType().GetMethod("SetProfile",Flags).Invoke(form,new object[]{new CommunityUser{email="ejemplo@example.org",displayName="Diseños Ana",bio="Diseños para corte y grabado.",emailVerified=true}});
                foreach(var size in new[]{new Size(940,650),new Size(760,550)})
                {
                    form.ClientSize=size;
                    for(int page=0;page<3;page++)
                    {
                        Field<TabControl>(form,"pages").SelectedIndex=page;
                        Field<TabControl>(form,"pages").SelectedTab.AutoScrollPosition=Point.Empty;
                        Save(form,Path.Combine(output,scheme+"-"+size.Width+"-page"+page+".png"));
                    }
                }
                form.GetType().GetMethod("Ask",Flags).Invoke(form,new object[]{"Login",new[]{"Email","Password"},null,1});
                foreach(var dialog in form.OwnedForms)
                { Save(dialog,Path.Combine(output,scheme+"-login.png")); dialog.Close(); }
                form.Close();
            }
        }
    }
}
'@
$sourcePath = Join-Path $renderOutput 'CommunitySnapshots.cs'
$libraryPath = Join-Path $renderOutput 'CommunitySnapshots.dll'
[IO.File]::WriteAllText($sourcePath,$source,(New-Object Text.UTF8Encoding($false)))
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compilerPath /nologo /codepage:65001 /target:library "/out:$libraryPath" "/reference:$applicationPath" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Community snapshot harness compilation failed.' }
[Reflection.Assembly]::LoadFrom($libraryPath) | Out-Null
[CommunitySnapshots]::Run($renderOutput)
Write-Output "Snapshots: $renderOutput"
