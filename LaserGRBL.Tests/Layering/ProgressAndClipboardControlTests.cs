using LaserGRBL;
using LaserGRBL.Layering;
using LaserGRBL.UserControls;
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace LaserGRBL.Tests.Layering
{
    [Collection("Job execution")]
    public class ProgressAndClipboardControlTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Private).GetValue(target); }
        private static object Call(object target, string name, params object[] args) { return target.GetType().GetMethod(name, Private).Invoke(target, args); }
        private static void Sta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        private static LaserDocument Document()
        {
            return new LaserDocument { Layers = { new LaserLayer { Name = "Test", VectorItems = {
                new VectorItem { Paths = { new VectorPath { Points = { new LaserPoint(0, 0), new LaserPoint(10, 10) } } } }
            } } } };
        }

        [Fact]
        public void JobProgress_PausedRepeatedUpdates_AssignsFinalTextOnceAndKeepsLayoutStable()
        {
            Sta(() =>
            {
                using (var sync = new Control())
                using (var view = new ConnectLogForm())
                {
                    var core = new GrblCore(sync, null, null);
                    var session = new JobExecutionSession(LayeredJobCompiler.Compile(Document(), 1000, "M4", "M5").OutputCommands, Document(), 3);
                    session.SetState(JobExecutionState.Paused);
                    typeof(GrblCore).GetProperty("JobSession").SetValue(core, session);
                    typeof(GrblCore).GetField("mLoopCount", Private).SetValue(core, 3m);
                    typeof(GrblCore).GetField("mJobError", Private).SetValue(core, "Test error");
                    typeof(ConnectLogForm).GetField("Core", Private).SetValue(view, core);
                    var bar = Field<DoubleProgressBar>(view, "PB");
                    bar.Bars.Add(new DoubleProgressBar.Bar(Color.Red)); bar.Bars.Add(new DoubleProgressBar.Bar(Color.Blue));
                    var summary = Field<Label>(view, "jobSummary"); var remaining = Field<Label>(view, "jobRemaining");
                    int summaries = 0, estimates = 0;
                    summary.TextChanged += (s, e) => summaries++; remaining.TextChanged += (s, e) => estimates++;
                    view.Width = 260; view.CreateControl();
                    Assert.True((bool)Call(view, "UpdateJobControls"));
                    Assert.Contains("1/3", summary.Text); Assert.Contains("Test error", remaining.Text);
                    Size size = Field<TableLayoutPanel>(view, "tableLayoutPanel5").Size;
                    for (int i = 0; i < 10; i++) Call(view, "UpdateJobControls");
                    Assert.Equal(1, summaries); Assert.Equal(1, estimates);
                    Assert.Equal(size, Field<TableLayoutPanel>(view, "tableLayoutPanel5").Size);
                    Assert.Equal(100, bar.Maximum);
                    Assert.Equal(core.GetJobProgress().SentPercent, bar.Bars[0].Value);
                    session.SetState(JobExecutionState.Resuming); Call(view, "UpdateJobControls");
                    session.SetState(JobExecutionState.Running); Call(view, "UpdateJobControls");
                    session.SetState(JobExecutionState.Completed); Call(view, "UpdateJobControls");
                    Assert.Equal(100, bar.Bars[1].Value); Assert.Equal("100 %", bar.PercString);
                    Assert.True(Field<Button>(view, "jobPause").Enabled == false);
                }
            });
        }

        [Theory]
        [InlineData(JobExecutionState.Running)]
        [InlineData(JobExecutionState.Paused)]
        public void Controller_CopyDuringJob_BlocksPasteAndDuplicate(JobExecutionState state)
        {
            Sta(() =>
            {
                using (var sync = new Control())
                {
                    var core = new GrblCore(sync, null, null); var document = Document();
                    typeof(LayerDocumentController).GetProperty("Document").SetValue(core.LayerController, document);
                    var session = new JobExecutionSession(new GrblCommand[0], document, 1); session.SetState(state);
                    typeof(GrblCore).GetProperty("JobSession").SetValue(core, session);
                    var controller = core.LayerController; var layer = document.Layers[0];
                    controller.CopyLayers(new[] { layer });
                    Assert.True(controller.Clipboard.HasContent);
                    Assert.Empty(controller.Paste()); Assert.Empty(controller.DuplicateLayers(new[] { layer }));
                    Assert.False(controller.SetPassOrder(JobPassOrder.ByObject)); Assert.Equal(JobPassOrder.ByLayer, document.PassOrder);
                    var objects = new[] { new VectorSelection(layer, layer.VectorItems[0], layer.VectorItems[0].Paths[0]) };
                    controller.CopyObjects(objects); Assert.True(controller.Clipboard.ContainsObjects);
                    Assert.Empty(controller.DuplicateObjects(objects)); Assert.Single(document.Layers);
                    Assert.False(document.IsDirty);
                }
            });
        }

        [Fact]
        public void Controller_Duplicate_DoesNotReplaceClipboardAndNotifiesOnce()
        {
            Sta(() =>
            {
                using (var sync = new Control())
                {
                    var core = new GrblCore(sync, null, null); var document = Document(); var controller = core.LayerController;
                    typeof(LayerDocumentController).GetProperty("Document").SetValue(controller, document);
                    var original = document.Layers[0]; controller.CopyLayers(new[] { original });
                    original.PowerPercent = 17;
                    int changes = 0; controller.DocumentChanged += () => changes++;
                    try
                    {
                        var copy = controller.DuplicateLayers(new[] { original }).Single();
                        Assert.Equal(1, changes); Assert.Equal(5, copy.TranslateX); Assert.Equal(17, copy.PowerPercent);
                        Assert.False(copy.OutputEnabled); Assert.False(controller.IsCompilationCurrent);
                        var paste = controller.Clipboard.Paste(document, false).Single();
                        Assert.Equal(5, paste.TranslateX); Assert.Equal(100, paste.PowerPercent);
                    }
                    finally { controller.ExitLayeredMode(); } // Cancel outstanding compile when tearing down the document.
                }
            });
        }

        [Fact]
        public void LayerGrid_ClipboardShortcuts_UseLayersAndRespectTextEditing()
        {
            Sta(() =>
            {
                using (var sync = new Control())
                using (var manager = new LayerManagerControl())
                {
                    var core = new GrblCore(sync, null, null); var document = Document();
                    typeof(LayerDocumentController).GetProperty("Document").SetValue(core.LayerController, document);
                    manager.SetCore(core); manager.SelectLayers(document.Layers);
                    var grid = Field<DataGridView>(manager, "grid"); manager.CreateControl(); grid.CreateControl();
                    var method = grid.GetType().GetMethod("ProcessCmdKey", Private);
                    object[] args = { new Message(), Keys.Control | Keys.C };
                    Assert.True((bool)method.Invoke(grid, args)); Assert.True(core.LayerController.Clipboard.HasContent);
                    grid.CurrentCell = grid.Rows[0].Cells["LayerName"];
                    Assert.True(grid.BeginEdit(false));
                    var editor = (TextBox)grid.EditingControl; editor.Text = "Editing name";
                    int commands = 0; grid.GetType().GetField("ClipboardCommand").SetValue(grid, new Action<Keys>(key => commands++));
                    args[1] = Keys.Control | Keys.D; method.Invoke(grid, args);
                    Assert.Equal(0, commands); Assert.True(grid.IsCurrentCellInEditMode); Assert.Equal("Editing name", editor.Text);
                    grid.CancelEdit();
                }
            });
        }

        [Fact]
        public void Controller_LoadDocument_ClearsClipboard()
        {
            Sta(() =>
            {
                using (var sync = new Control())
                {
                    var core = new GrblCore(sync, null, null); var controller = core.LayerController; var document = Document();
                    typeof(LayerDocumentController).GetProperty("Document").SetValue(controller, document);
                    controller.CopyLayers(document.Layers);
                    string filename = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".lps");
                    try
                    {
                        LayerProjectSerializer.Save(filename, document);
                        // Cancel the compile during teardown's notification: this test checks document lifetime only.
                        controller.CompilationStateChanged += () => { if (!controller.IsCompilationCurrent) controller.ExitLayeredMode(); };
                        controller.Load(filename); Assert.False(controller.Clipboard.HasContent);
                    }
                    finally { controller.ExitLayeredMode(); if (System.IO.File.Exists(filename)) System.IO.File.Delete(filename); }
                }
            });
        }

        [Fact]
        public void PassOrderDialog_SpanishChoicesAndCancel_KeepButtonsVisibleAndReturnSelectedMode()
        {
            Sta(() =>
            {
                Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("es-MX");
                var type = typeof(LaserDocument).Assembly.GetType("LaserGRBL.Layering.JobPassOrderDialog");
                using (var dialog = (Form)Activator.CreateInstance(type, new object[] { JobPassOrder.ByLayer }))
                {
                    dialog.CreateControl(); dialog.PerformLayout();
                    Assert.Equal(JobPassOrder.ByLayer, type.GetProperty("PassOrder").GetValue(dialog));
                    var layout = (TableLayoutPanel)dialog.Controls[0];
                    var radios = layout.Controls.OfType<RadioButton>().ToList(); Assert.Equal(2, radios.Count);
                    Assert.Contains("Por objeto", radios[0].Text); Assert.Contains("Por capa", radios[1].Text);
                    radios[0].Checked = true;
                    Assert.Equal(JobPassOrder.ByObject, type.GetProperty("PassOrder").GetValue(dialog));
                    Assert.Equal(DialogResult.Cancel, ((Button)dialog.CancelButton).DialogResult);
                    Assert.Equal(DialogResult.OK, ((Button)dialog.AcceptButton).DialogResult);
                    Assert.True(layout.GetPreferredSize(Size.Empty).Height <= dialog.ClientSize.Height);
                }
            });
        }

        [Fact]
        public void Controller_SetPassOrder_RegeneratesCommandsAndKeepsPreviousJobOnValidationFailure()
        {
            Sta(() =>
            {
                using (var sync = new Control())
                {
                    var core = new GrblCore(sync, null, null); var controller = core.LayerController; var document = Document();
                    document.Layers[0].Passes = 2;
                    document.Layers[0].VectorItems[0].Paths.Add(document.Layers[0].VectorItems[0].Paths[0].Clone());
                    typeof(LayerDocumentController).GetProperty("Document").SetValue(controller, document);
                    int notifications = 0; controller.DocumentChanged += () => notifications++;
                    Assert.True(controller.SetPassOrder(JobPassOrder.ByObject));
                    Assert.Equal(JobPassOrder.ByObject, document.PassOrder); Assert.Equal(1, notifications); Assert.True(document.IsDirty);
                    Assert.Equal(new[] { 0, 0, 1, 1 }, core.LoadedFile.Commands.Where(c => c.IsJobCheckpoint).Select(c => c.SourceObjectIndex));
                    Assert.True(controller.SetPassOrder(JobPassOrder.ByObject, 10));
                    Assert.Equal(40, core.LoadedFile.Commands.Count(c => c.IsJobCheckpoint));
                    Assert.True(controller.SetPassOrder(JobPassOrder.ByObject, 1));
                    Assert.Equal(4, core.LoadedFile.Commands.Count(c => c.IsJobCheckpoint));
                    Assert.Equal(2, document.Layers[0].Passes);
                    var commands = core.LoadedFile.Commands.Select(c => c.Command).ToList();
                    document.Layers[0].SpeedMmPerMinute = 0;
                    Assert.False(controller.SetPassOrder(JobPassOrder.ByLayer));
                    Assert.Equal(JobPassOrder.ByObject, document.PassOrder);
                    Assert.Equal(commands, core.LoadedFile.Commands.Select(c => c.Command)); Assert.Equal(3, notifications);
                }
            });
        }

        [Fact]
        public void Controller_NewDocumentAndExit_ClearClipboard()
        {
            Sta(() =>
            {
                using (var sync = new Control())
                {
                    var core = new GrblCore(sync, null, null); var controller = core.LayerController; var document = Document();
                    typeof(LayerDocumentController).GetProperty("Document").SetValue(controller, document);
                    controller.CopyLayers(document.Layers); controller.NewDocument(); Assert.False(controller.Clipboard.HasContent);
                    controller.Document.Layers.Add(document.Layers[0]); controller.CopyLayers(controller.Document.Layers);
                    controller.ExitLayeredMode(); Assert.False(controller.Clipboard.HasContent);
                }
            });
        }
    }
}
