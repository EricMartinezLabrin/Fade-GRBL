using LaserGRBL;
using LaserGRBL.ComWrapper;
using LaserGRBL.Layering;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace LaserGRBL.Tests.Layering
{
    [CollectionDefinition("Job execution", DisableParallelization = true)]
    public class JobExecutionCollection { }

    [Collection("Job execution")]
    public class JobExecutionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object Call(object target, string method, params object[] args)
        { return typeof(GrblCore).GetMethod(method, Private).Invoke(target, args); }
        private static T Field<T>(object target, string field) { return (T)typeof(GrblCore).GetField(field, Private).GetValue(target); }
        private static void Set(object target, string field, object value) { typeof(GrblCore).GetField(field, Private).SetValue(target, value); }

        private static LaserDocument Document(int passes = 8)
        {
            LaserDocument document = new LaserDocument();
            LaserLayer layer = new LaserLayer { Color = Color.Red, Name = "Rojo", Passes = passes, SpeedMmPerMinute = 600, PowerPercent = 50 };
            VectorPath path = new VectorPath();
            for (int i = 0; i <= 12; i++) path.Points.Add(new LaserPoint(i, i % 2));
            VectorItem item = new VectorItem(); item.Paths.Add(path); layer.VectorItems.Add(item); document.Layers.Add(layer);
            return document;
        }

        private static GrblFile Compile(LaserDocument document)
        {
            LayeredCompilation result = LayeredJobCompiler.Compile(document, 1000, "M4", "M5");
            Assert.False(result.HasErrors);
            GrblFile file = new GrblFile();
            typeof(GrblFile).GetMethod("LoadCommands", Private).Invoke(file, new object[] { result.OutputCommands, "Test", false });
            return file;
        }

        private static JobExecutionSession Session(LaserDocument document, int cycles = 1)
        { return new JobExecutionSession(Compile(document), document, cycles); }

        [Fact]
        public void Compile_PassesHaveStableMetadataAndIndependentClones()
        {
            LaserDocument document = Document(3);
            var commands = Compile(document).ToList();
            Assert.Equal(3, commands.Count(c => c.IsJobCheckpoint));
            foreach (int pass in new[] { 1, 2, 3 })
            {
                var group = commands.Where(c => c.SourcePass == pass).ToList();
                Assert.True(group[0].IsJobCheckpoint);
                Assert.Equal(12, group.Count(c => c.IsCuttingMovement));
                Assert.Equal(Enumerable.Range(1, 12), group.Where(c => c.IsCuttingMovement).Select(c => c.SourceMovement));
            }
            GrblCommand clone = (GrblCommand)commands.First(c => c.IsCuttingMovement).Clone();
            Assert.Equal(document.Layers[0].Id, clone.SourceLayerId);
            Assert.Equal(1, clone.SourcePass); Assert.Equal(1, clone.SourceMovement);
        }

        [Fact]
        public void Rewind_FiveCuttingMovements_DoesNotCountPowerOrRapids()
        {
            var session = Session(Document(3));
            int current = session.Commands.FindIndex(c => c.SourcePass == 3 && c.IsCuttingMovement && c.SourceMovement == 9);
            session.Sent(session.Commands.Last()); session.ReportLine(current + 1);
            int target = session.RewindTarget(5);
            Assert.Equal(4, session.Commands[target].SourceMovement);
            Assert.Equal(3, session.Commands[target].SourcePass);
            Assert.Equal(session.PassStart, session.RewindTarget(1000));
        }

        [Fact]
        public void Progress_TransmittedCommandsDoNotCompleteJob_AndIncludeGlobalCycles()
        {
            var session = Session(Document(2), 2);
            session.Sent(session.Commands.Last());
            Assert.True(session.Snapshot().Percent < 50);
            Assert.Equal(50, session.Snapshot().SentPercent, 2);
            session.NextCycle(session.Commands);
            Assert.Equal(50, session.Snapshot().Percent, 2);
            session.Sent(session.Commands.Last()); session.ReportLine(session.Commands.Count);
            Assert.True(session.Snapshot().Percent < 100);
            session.SetState(JobExecutionState.Completed);
            Assert.Equal(100, session.Snapshot().Percent);
            Assert.Equal(2, session.Snapshot().CompletedPasses);
        }

        [Fact]
        public void Pause_FreezesEstimateAndPercent_AndUsesThirtyPercentMargin()
        {
            var session = Session(Document(2));
            var estimate = session.Snapshot().Remaining.Value;
            Assert.InRange(estimate.TotalSeconds, session.EstimatedSeconds * 1.299, session.EstimatedSeconds * 1.301);
            session.Sent(session.Commands.Last());
            session.SetState(JobExecutionState.Pausing); session.SetState(JobExecutionState.Paused);
            var paused = session.Snapshot(); Thread.Sleep(30);
            Assert.Equal(paused.Remaining, session.Snapshot().Remaining);
            Assert.Equal(paused.Percent, session.Snapshot().Percent);
        }

        [Fact]
        public void Snapshot_SeparatesActiveLayerPassAndCompletedLayers()
        {
            LaserDocument doc = Document(2); LaserLayer second = Document(4).Layers[0]; second.Color = Color.Blue; second.Name = "Azul"; second.Order = 1; doc.Layers.Add(second);
            var session = Session(doc);
            var checkpoint = session.Commands.First(c => c.SourceLayerId == second.Id && c.SourcePass == 3 && c.IsJobCheckpoint);
            session.Checkpoint(checkpoint);
            Assert.True(session.IsLayerCompleted(doc.Layers[0].Id));
            Assert.False(session.IsLayerCompleted(second.Id));
            Assert.Equal("Azul", session.Snapshot().LayerName);
            Assert.Equal(3, session.Snapshot().Pass); Assert.Equal(4, session.Snapshot().Passes); Assert.Equal(2, session.Snapshot().CompletedPasses);
        }

        [Fact]
        public void ReplaceCommands_ChangesPowerSpeedPasses_WithoutMutatingOldSnapshot()
        {
            LaserDocument doc = Document(8); var session = Session(doc);
            LaserDocument edited = doc.Clone(); edited.Layers[0].SpeedMmPerMinute = 300; edited.Layers[0].PowerPercent = 75; edited.Layers[0].Passes = 3;
            GrblFile prepared = Compile(edited);
            Assert.Equal(8, session.Document.Layers[0].Passes);
            session.ReplaceCommands(prepared, edited, 0);
            Assert.Equal(3, session.Commands.Count(c => c.IsJobCheckpoint));
            Assert.Contains(session.Commands, c => c.Command == "F300"); Assert.Contains(session.Commands, c => c.Command == "M4 S750");
            Assert.Equal(8, doc.Layers[0].Passes);
            edited.Layers[0].Passes = 99; Assert.Equal(3, session.Document.Layers[0].Passes);
        }

        [Fact]
        public void Raster_AdjustedPowerPreservesModulationAndZeroPower()
        {
            LaserLayer raster = new LaserLayer { Name = "Raster", Color = Color.Red, ContentKind = LayerContentKind.Raster, Mode = LayerMode.Image,
                PowerPercent = 80, SpeedMmPerMinute = 432, Passes = 3, RasterItem = new RasterItem { OriginalMaxPower = 1000, OriginalMinPower = 0,
                    BaseCommands = { "G0 X0 Y0", "M4 S0", "F100", "G1 X1 S0", "X2 S500", "X3 S1000", "M5" } } };
            LaserDocument doc = new LaserDocument(); doc.Layers.Add(raster);
            var result = LayeredJobCompiler.Compile(doc, 1000, "M4", "M5");
            Assert.Contains(result.OutputCommands, c => c.Command == "X2 S400");
            Assert.Contains(result.OutputCommands, c => c.Command == "X3 S800");
            Assert.Contains(result.OutputCommands, c => c.Command == "G1 X1 S0");
            Assert.Contains(result.OutputCommands, c => c.Command == "F432");
            Assert.Equal(6, result.OutputCommands.Count(c => c.IsCuttingMovement));
        }

        [Fact]
        public void Protocol_HoldMustFinishBeforeEditing_AndBlocksTransmission()
        {
            using (var harness = new Harness())
            {
                harness.Core.FeedHold(false);
                Assert.Equal(JobExecutionState.Pausing, harness.Core.JobSession.State);
                Assert.True((bool)Call(harness.Core, "BlockJobTransmission"));
                Call(harness.Core, "OnJobRealtimeStatus", "Hold:1|MPos:2,0,0|WCO:0,0,0");
                Assert.False(harness.Core.CanAdjustJob);
                Call(harness.Core, "OnJobRealtimeStatus", "Hold:0|MPos:2,0,0|WCO:0,0,0");
                Assert.True(harness.Core.CanAdjustJob);
                Assert.Contains((byte)33, harness.Wire.Bytes);
            }
        }

        [Fact]
        public void Protocol_MissingLineNumber_RebuildUsesPassCheckpointAndWaitsForStartup()
        {
            using (var harness = new Harness())
            {
                harness.Pause();
                Call(harness.Core, "StartJobRebuild", 5);
                Assert.Equal(JobExecutionState.Resuming, harness.Core.JobSession.State);
                Assert.Contains((byte)24, harness.Wire.Bytes);
                Assert.Empty(harness.Queue);
                Call(harness.Core, "OnJobRealtimeStatus", "Idle|MPos:2,0,0|WCO:0,0,0");
                Assert.Empty(harness.Queue); // A stale Idle report before reset acknowledgement cannot authorize motion.
                Call(harness.Core, "OnStartupMessage");
                Call(harness.Core, "OnJobRealtimeStatus", "Idle|MPos:2,0,0|WCO:0,0,0");
                Assert.Contains(harness.Queue, c => c.Command.StartsWith("G92"));
                Assert.DoesNotContain(harness.Queue, c => c.Command.StartsWith("G0 "));
                var marker = harness.Queue.Last(); harness.Queue.Clear();
                marker.SetResult("ok", false); Call(harness.Core, "OnJobCommandResponse", marker);
                Call(harness.Core, "OnJobRealtimeStatus", "Idle|MPos:2,0,0|WCO:0,0,0");
                Assert.Equal(JobExecutionState.Running, harness.Core.JobSession.State);
                Assert.Contains(harness.Queue, c => c.Command.StartsWith("G0 "));
                Assert.True(harness.Queue.First().Command == "M5");
                Assert.True(harness.Core.JobEditingLocked);
            }
        }

        [Fact]
        public void Protocol_ChangedPositionAfterReset_StopsAndPreservesOriginalDocument()
        {
            using (var harness = new Harness())
            {
                harness.Pause(); var original = harness.Core.LayerController.Document;
                Call(harness.Core, "StartJobRebuild", 5); Call(harness.Core, "OnStartupMessage");
                Set(harness.Core, "mMPos", new GPoint(99, 0, 0));
                Call(harness.Core, "OnJobRealtimeStatus", "Idle|MPos:99,0,0|WCO:0,0,0");
                Assert.Equal(JobExecutionState.Interrupted, harness.Core.JobSession.State);
                Assert.Empty(harness.Queue); Assert.Same(original, harness.Core.LayerController.Document);
                Assert.False(string.IsNullOrEmpty(harness.Core.JobControlError));
            }
        }

        [Fact]
        public void Protocol_UnsafeStartupBlocks_DisablesRebuildButRetainsNativeResume()
        {
            using (var harness = new Harness())
            {
                Call(harness.Core, "ReadJobStartupLine", "$N0=M3 S1000"); harness.Pause();
                Assert.False(harness.Core.CanAdjustJob);
                Call(harness.Core, "CycleStartResumeNative");
                Assert.Contains((byte)126, harness.Wire.Bytes);
                Assert.Equal(JobExecutionState.Resuming, harness.Core.JobSession.State);
            }
        }

        private sealed class Harness : IDisposable
        {
            public readonly GrblCore Core;
            public readonly FakeWire Wire = new FakeWire();
            public Queue<GrblCommand> Queue { get { return Field<Queue<GrblCommand>>(Core, "mQueuePtr"); } }
            private readonly object oldConfig = Settings.GetObject<object>("Grbl Configuration ST", null);
            private readonly object oldVersion = Settings.GetObject<object>("Last GrblVersion known", null);
            private readonly Control sync = new Control();

            public Harness()
            {
                var version = new GrblCore.GrblVersionInfo(1, 1, 'h');
                Settings.SetObject("Last GrblVersion known", version);
                GrblCore.Configuration = new GrblConfST(version, new Dictionary<int, string> { { 32, "1" }, { 30, "1000" }, { 110, "1000" } });
                Core = new GrblCore(sync, null, null);
                Set(Core, "com", Wire); Set(Core, "mMachineStatus", GrblCore.MacStatus.Run);
                LaserDocument doc = Document(3);
                typeof(LayerDocumentController).GetProperty("Document").SetValue(Core.LayerController, doc);
                Set(Core, "file", Compile(doc));
                Call(Core, "BeginJobSession", true, false);
                Queue.Clear();
                foreach (GrblCommand command in Core.JobSession.Commands) Core.JobSession.Sent(command);
                Core.JobSession.Checkpoint(Core.JobSession.Commands.First());
                Set(Core, "mMPos", new GPoint(2, 0, 0));
                Call(Core, "ReadJobStartupLine", "$N0="); Call(Core, "ReadJobStartupLine", "$N1=");
            }
            public void Pause()
            {
                Core.FeedHold(false);
                Set(Core, "mMachineStatus", GrblCore.MacStatus.Hold);
                Call(Core, "OnJobRealtimeStatus", "Hold:0|MPos:2,0,0|WCO:0,0,0");
            }
            public void Dispose()
            {
                sync.Dispose(); Settings.SetObject("Grbl Configuration ST", oldConfig); Settings.SetObject("Last GrblVersion known", oldVersion);
            }
        }

        private sealed class FakeWire : IComWrapper
        {
            public readonly List<byte> Bytes = new List<byte>();
            public bool IsOpen { get { return true; } }
            public void Write(byte value) { Bytes.Add(value); }
            public void Write(byte[] values) { Bytes.AddRange(values); }
            public void Write(string value) { }
            public void Configure(params object[] args) { }
            public void Open() { } public void Close(bool auto) { }
            public bool HasData() { return false; } public string ReadLineBlocking() { return null; }
        }
    }
}
