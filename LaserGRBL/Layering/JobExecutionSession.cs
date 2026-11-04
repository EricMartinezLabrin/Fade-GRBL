using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LaserGRBL.Layering
{
    public enum JobExecutionState { Running, Pausing, Paused, Resuming, Completed, Interrupted }

    public sealed class JobProgressSnapshot
    {
        public JobExecutionState State { get; internal set; }
        public double Percent { get; internal set; }
        public double SentPercent { get; internal set; }
        public string LayerName { get; internal set; }
        public int Pass { get; internal set; }
        public int Passes { get; internal set; }
        public int CompletedPasses { get; internal set; }
        public int Cycle { get; internal set; }
        public int Cycles { get; internal set; }
        public TimeSpan? Remaining { get; internal set; }
    }

    // Owns a snapshot of the job. Editing the document cannot alter an active queue.
    // Acknowledgement is deliberately NOT treated as physical completion.
    public sealed class JobExecutionSession
    {
        private readonly Stopwatch active = new Stopwatch();
        private readonly Dictionary<Guid, double> slowestLayerRatio = new Dictionary<Guid, double>();
        private double sampleTime, sampleEstimate;
        private Guid? sampleLayer;
        private TimeSpan? frozenRemaining;
        private double completedWeight;
        public List<GrblCommand> Commands { get; private set; }
        public LaserDocument Document { get; private set; }
        public JobExecutionState State { get; private set; }
        public bool Active { get { return State != JobExecutionState.Completed && State != JobExecutionState.Interrupted; } }
        public int Cycle { get; private set; }
        public int Cycles { get; private set; }
        public int ConfirmedIndex { get; private set; }
        public int CurrentIndex { get; private set; }
        public int SentIndex { get; private set; }
        public bool HasReportedLine { get; private set; }
        public bool CanNumberLines { get; private set; }
        public double EstimatedSeconds { get { return Commands.Count == 0 ? 0 : Commands[Commands.Count - 1].TimeOffset.TotalSeconds; } }

        public JobExecutionSession(IEnumerable<GrblCommand> commands, LaserDocument document, int cycles)
        {
            Cycles = Math.Max(1, cycles); Cycle = 1;
            ReplaceCommands(commands, document, 0);
            SetState(JobExecutionState.Running);
        }

        public void ReplaceCommands(IEnumerable<GrblCommand> commands, LaserDocument document, int position)
        {
            Commands = commands.Select(command => (GrblCommand)command.Clone()).ToList();
            for (int i = 0; i < Commands.Count; i++) { Commands[i].JobIndex = i; Commands[i].ReportJobLine = false; }
            CanNumberLines = Commands.Count < 10000000 && Commands.All(c => c.SerialData.Length + 9 <= 80);
            Document = document == null ? null : document.Clone();
            CurrentIndex = ConfirmedIndex = Math.Max(0, Math.Min(position, Commands.Count));
            SentIndex = CurrentIndex; HasReportedLine = false;
            sampleLayer = null; sampleTime = active.Elapsed.TotalSeconds; sampleEstimate = Offset(CurrentIndex);
            frozenRemaining = null;
        }

        public void NextCycle(IEnumerable<GrblCommand> commands)
        {
            completedWeight += Weight(); Cycle++;
            ReplaceCommands(commands, Document, 0);
            SetState(JobExecutionState.Running);
        }

        public void SetCyclesRemaining(int remaining) { Cycles = Cycle - 1 + Math.Max(1, remaining); }

        public void SetState(JobExecutionState state)
        {
            if (state == JobExecutionState.Pausing && State != state) frozenRemaining = CalculateRemaining();
            State = state;
            if (state == JobExecutionState.Running) { frozenRemaining = null; active.Start(); }
            else if (state == JobExecutionState.Paused && !frozenRemaining.HasValue) { frozenRemaining = CalculateRemaining(); active.Stop(); }
            else active.Stop();
        }

        public void Sent(GrblCommand command) { if (command.JobIndex >= 0) SentIndex = Math.Max(SentIndex, command.JobIndex + 1); }

        public void Checkpoint(GrblCommand command)
        {
            if (!command.IsJobCheckpoint || command.JobIndex < 0) return;
            ConfirmedIndex = Math.Max(ConfirmedIndex, command.JobIndex + 1);
            Advance(ConfirmedIndex);
        }

        public void ReportLine(int line)
        {
            int index = line - 1;
            if (index < ConfirmedIndex || index >= SentIndex || index >= Commands.Count) return;
            HasReportedLine = true;
            Advance(index);
        }

        public void ForgetReportedLine() { HasReportedLine = false; }

        private void Advance(int index)
        {
            CurrentIndex = Math.Max(CurrentIndex, Math.Min(index, Commands.Count));
            GrblCommand current = CurrentCommand;
            if (current == null) return;
            double estimate = Offset(CurrentIndex), seconds = active.Elapsed.TotalSeconds;
            if (sampleLayer.HasValue && estimate - sampleEstimate >= 1)
            {
                double ratio = (seconds - sampleTime) / (estimate - sampleEstimate), previous;
                slowestLayerRatio.TryGetValue(sampleLayer.Value, out previous);
                slowestLayerRatio[sampleLayer.Value] = Math.Max(previous, ratio);
            }
            if (sampleLayer != current.SourceLayerId || estimate - sampleEstimate >= 1)
            { sampleLayer = current.SourceLayerId; sampleEstimate = estimate; sampleTime = seconds; }
        }

        public GrblCommand CurrentCommand { get { return Commands.Count == 0 ? null : Commands[Math.Min(CurrentIndex, Commands.Count - 1)]; } }
        public int PassStart
        {
            get
            {
                GrblCommand current = CurrentCommand;
                if (current == null) return 0;
                int i = Math.Min(CurrentIndex, Commands.Count - 1);
                while (i > 0 && Commands[i - 1].SourceLayerId == current.SourceLayerId && Commands[i - 1].SourcePass == current.SourcePass) i--;
                return i;
            }
        }

        public int RewindTarget(int movements)
        {
            int start = PassStart, i = Math.Min(CurrentIndex, Commands.Count - 1);
            while (i > start && movements > 0) { i--; if (Commands[i].IsCuttingMovement) movements--; }
            return i;
        }

        public int AvailableMovements { get { return Commands.Skip(PassStart).Take(Math.Max(0, CurrentIndex - PassStart)).Count(c => c.IsCuttingMovement); } }
        public bool IsLayerCompleted(Guid layer)
        {
            int last = Commands.FindLastIndex(c => c.SourceLayerId == layer);
            return last < 0 || last < CurrentIndex;
        }

        private double Offset(int index) { return index <= 0 || Commands.Count == 0 ? 0 : Commands[Math.Min(index, Commands.Count) - 1].TimeOffset.TotalSeconds; }
        private double Weight() { return EstimatedSeconds > 0 ? EstimatedSeconds : Math.Max(1, Commands.Count); }
        private double DoneWeight(int index) { return EstimatedSeconds > 0 ? Offset(index) : index; }

        private double DisplayDoneWeight()
        {
            double done = DoneWeight(CurrentIndex);
            if (HasReportedLine || EstimatedSeconds <= 0 || !Active) return done;
            // Between confirmed checkpoints this is an estimate, bounded by transmitted work.
            int nextCheckpoint = Commands.FindIndex(Math.Min(CurrentIndex, Commands.Count), c => c.IsJobCheckpoint);
            int end = nextCheckpoint < 0 ? SentIndex : Math.Min(SentIndex, nextCheckpoint);
            double available = Math.Max(0, Offset(end) - done);
            double ratio = Math.Max(1, slowestLayerRatio.Count == 0 ? 1 : slowestLayerRatio.Values.Max());
            return done + Math.Min(available * .9, Math.Max(0, active.Elapsed.TotalSeconds - sampleTime) / ratio);
        }

        private TimeSpan? CalculateRemaining()
        {
            if (State == JobExecutionState.Completed) return TimeSpan.Zero;
            if (EstimatedSeconds <= 0) return null;
            double remaining = 0, last = Offset(CurrentIndex);
            for (int i = CurrentIndex; i < Commands.Count; i++)
            {
                double end = Commands[i].TimeOffset.TotalSeconds, ratio = 1;
                if (Commands[i].SourceLayerId.HasValue) slowestLayerRatio.TryGetValue(Commands[i].SourceLayerId.Value, out ratio);
                remaining += Math.Max(0, end - last) * Math.Max(1, ratio); last = end;
            }
            double worst = Math.Max(1, slowestLayerRatio.Count == 0 ? 1 : slowestLayerRatio.Values.Max());
            remaining = Math.Max(0, remaining - Math.Max(0, DisplayDoneWeight() - Offset(CurrentIndex)));
            remaining += Math.Max(0, Cycles - Cycle) * EstimatedSeconds * worst;
            return TimeSpan.FromSeconds(Math.Min(TimeSpan.MaxValue.TotalSeconds / 2, remaining * 1.30));
        }

        public JobProgressSnapshot Snapshot()
        {
            GrblCommand command = CurrentCommand;
            LaserLayer layer = Document == null || command == null ? null : Document.Layers.FirstOrDefault(l => l.Id == command.SourceLayerId);
            double denominator = completedWeight + Weight() * Math.Max(1, Cycles - Cycle + 1);
            bool completed = State == JobExecutionState.Completed;
            return new JobProgressSnapshot {
                State = State, Percent = completed ? 100 : Math.Min(99, 100 * (completedWeight + DisplayDoneWeight()) / denominator),
                SentPercent = completed ? 100 : Math.Min(100, 100 * (completedWeight + DoneWeight(SentIndex)) / denominator),
                LayerName = layer == null ? null : layer.Name, Pass = command == null ? 0 : command.SourcePass,
                Passes = layer == null ? 0 : layer.Passes, CompletedPasses = layer == null ? 0 : completed ? layer.Passes : Math.Max(0, command.SourcePass - 1),
                Cycle = Cycle, Cycles = Cycles,
                Remaining = completed ? TimeSpan.Zero : State == JobExecutionState.Paused || State == JobExecutionState.Pausing ? frozenRemaining : CalculateRemaining()
            };
        }
    }
}
