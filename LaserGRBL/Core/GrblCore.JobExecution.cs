using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using LaserGRBL.Layering;

namespace LaserGRBL
{
    public partial class GrblCore
    {
        public JobExecutionSession JobSession { get; private set; }
        public bool JobEditingLocked { get { return JobSession != null && JobSession.Active; } }
        private bool mJobPauseRequested, mJobStopped, mJobAwaitIdle, mJobFooterQueued;
        private int mJobStartupMask;
        private bool mJobStartupSafe;
        private LaserDocument mJobStagedDocument;
        private GPoint mJobPausePosition, mJobPauseWco;
        private string mJobError;
        private JobRebuild mJobRebuild;
        private int mPreparedObjectRepetitions = 1;
        private decimal JobCyclesRemaining
        { get { return JobSession != null && JobSession.Document != null && JobSession.Document.PassOrder == JobPassOrder.ByObject ? 1 : mLoopCount; } }

        private sealed class JobRebuild
        {
            public GrblFile File;
            public LaserDocument Document;
            public LayeredCompilation Compilation;
            public int Position;
            public int Stage; // 0: startup, 1: stationary position, 2: restoring WCO, 3: verify WCO
            public long Deadline;
            public GPoint PositionBeforeReset, WcoBeforeReset;
            public bool ContinueInsideLine;
        }

        public bool CanAdjustJob
        {
            get { lock (this) return JobEditingLocked && mJobStopped && mJobRebuild == null &&
                JobSession.State == JobExecutionState.Paused && !mJobFooterQueued && SupportsJobRebuild; }
        }

        public string JobControlError { get { lock (this) return mJobError; } }
        public bool HasStagedJobParameters { get { lock (this) return mJobStagedDocument != null; } }
        private bool IsGrbl11 { get { return Type == Firmware.Grbl && GrblVersion != null && GrblVersion >= new GrblVersionInfo(1, 1); } }
        private bool SupportsJobRebuild { get { return IsGrbl11 && Configuration.LaserMode && JobSession.Document != null &&
            JobSession.Commands.Count < 10000000 && mJobStartupMask == 3 && mJobStartupSafe; } }

        public JobProgressSnapshot GetJobProgress()
        {
            lock (this) { if (JobSession == null) return null; JobSession.SetCyclesRemaining((int)mLoopCount); return JobSession.Snapshot(); }
        }

        private bool ChooseJobPassOrder(Form owner)
        {
            if (!LayerController.IsLayered) return true;
            using (var dialog = new JobPassOrderDialog(LayerController.Document.PassOrder))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
                try
                {
                    int repetitions = dialog.PassOrder == JobPassOrder.ByObject ? Math.Max(1, (int)mLoopCount) : 1;
                    if (LayerController.SetPassOrder(dialog.PassOrder, repetitions))
                    { mPreparedObjectRepetitions = repetitions; return true; }
                }
                catch (Exception ex) { Logger.LogException("Pass order", ex); }
                MessageBox.Show(owner, LayerText.Get("JobCompileFailed"), LayerText.Get("PassOrderTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void BeginJobSession(bool first, bool pass)
        {
            if (pass && JobSession != null && JobSession.Active) JobSession.NextCycle(file);
            else
            {
                JobSession = new JobExecutionSession(file, LayerController.Document, (int)mLoopCount, mPreparedObjectRepetitions);
                mJobError = null; mJobStagedDocument = null;
            }
            mJobPauseRequested = mJobStopped = mJobAwaitIdle = mJobFooterQueued = false;
            if (IsGrbl11 && first)
            {
                mJobStartupMask = 0; mJobStartupSafe = true;
                mQueuePtr.Enqueue(new GrblCommand("$N"));
            }
        }

        private GrblCommand PrepareJobCommand(GrblCommand original)
        {
            GrblCommand command = (GrblCommand)original.Clone();
            command.ClearResult();
            command.ReportJobLine = IsGrbl11 && JobSession.Document != null && JobSession.CanNumberLines;
            return command;
        }

        private bool BlockJobTransmission()
        {
            if (mJobRebuild != null) return mJobRebuild.Stage != 2;
            return mJobPauseRequested;
        }

        private void InterruptJobSession()
        {
            lock (this)
            {
                if (JobSession != null && JobSession.Active) JobSession.SetState(JobExecutionState.Interrupted);
                mJobPauseRequested = mJobStopped = mJobAwaitIdle = false;
                mJobStagedDocument = null; mJobRebuild = null;
            }
        }

        private void FailJobRebuild(string message)
        {
            mJobError = message;
            ClearQueue(false); mUsedBuffer = 0;
            mTP.JobEnd(true);
            SendImmediate(33);
            InterruptJobSession();
            SetIssue(DetectedIssue.ManualReset);
            Logger.LogMessage("JobResume", message);
        }

        private void OnJobStartup()
        {
            if (mJobRebuild != null && mJobRebuild.Stage == 0) mJobRebuild.Stage = 1;
            else if (JobSession != null && JobSession.Active) InterruptJobSession();
        }

        private void TickJobRecovery()
        {
            if (mJobRebuild != null && Tools.HiResTimer.TotalMilliseconds > mJobRebuild.Deadline)
                FailJobRebuild(LayerText.Get("JobRecoveryTimeout"));
        }

        private void ReadJobStartupLine(string line)
        {
            if (!line.StartsWith("$N0=") && !line.StartsWith("$N1=")) return;
            mJobStartupMask |= line[2] == '0' ? 1 : 2;
            // Startup blocks run after a soft reset. Never reset a board with executable startup code.
            if (line.Substring(4).Trim().Length != 0) mJobStartupSafe = false;
        }

        private void OnJobCommandResponse(GrblCommand command)
        {
            if (mJobRebuild != null)
            {
                if (command.Status != GrblCommand.CommandStatus.ResponseGood) { FailJobRebuild(LayerText.Get("JobRecoveryFailed")); return; }
                if (mJobRebuild.Stage == 2 && command.IsJobCheckpoint) { mJobRebuild.Stage = 3; QueryPosition(); }
                return;
            }
            if (JobSession == null || !JobSession.Active) return;
            if (command.Status == GrblCommand.CommandStatus.ResponseGood) JobSession.Checkpoint(command);
            else if (command.IsJobCheckpoint) { mJobError = LayerText.Get("JobCheckpointFailed"); mJobStartupSafe = false; }
        }

        private void RequestJobEnd()
        {
            if (!IsGrbl11 || MachineStatus == MacStatus.Check) { OnProgramEnd(); return; }
            if (!mJobFooterQueued && JobCyclesRemaining <= 1)
            {
                mJobFooterQueued = true;
                OnJobEnd();
                mQueuePtr.Enqueue(new GrblCommand("M5"));
                mQueuePtr.Enqueue(new GrblCommand("G4 P0"));
                return;
            }
            mJobAwaitIdle = true;
            QueryPosition();
        }

        private void OnJobRealtimeStatus(string report)
        {
            if (JobSession == null || !JobSession.Active) return;
            string[] fields = report.Split('|');
            string state = fields[0];
            bool hasPosition = fields.Any(f => f.StartsWith("MPos:") || f.StartsWith("WPos:"));
            if (state.StartsWith("Alarm") || state == "Disconnected")
            {
                if (mJobRebuild != null) FailJobRebuild(LayerText.Get("JobRecoveryFailed"));
                else InterruptJobSession();
                return;
            }
            if (mJobRebuild != null)
            {
                if (state != "Idle" || !hasPosition) return;
                if (!SameJobPosition(MachinePosition, mJobRebuild.PositionBeforeReset)) { FailJobRebuild(LayerText.Get("JobPositionChanged")); return; }
                if (mJobRebuild.Stage == 1)
                {
                    GPoint work = mJobRebuild.PositionBeforeReset - mJobRebuild.WcoBeforeReset;
                    mJobRebuild.Stage = 2;
                    mQueuePtr.Enqueue(new GrblCommand("M5"));
                    mQueuePtr.Enqueue(new GrblCommand("G21"));
                    mQueuePtr.Enqueue(new GrblCommand("G90"));
                    mQueuePtr.Enqueue(new GrblCommand(JobPointCommand("G92", work)));
                    mQueuePtr.Enqueue(new GrblCommand("G4 P0") { IsJobCheckpoint = true });
                }
                else if (mJobRebuild.Stage == 3 && fields.Any(f => f.StartsWith("WCO:")))
                {
                    if (!SameJobPosition(mWCO, mJobRebuild.WcoBeforeReset)) { FailJobRebuild(LayerText.Get("JobPositionChanged")); return; }
                    CommitJobRebuild();
                }
                return;
            }
            bool stoppedLine = false;
            foreach (string field in fields)
            {
                int line;
                if (JobSession.Document != null && JobSession.CanNumberLines && field.StartsWith("Ln:") && int.TryParse(field.Substring(3), out line) && line > 0)
                { JobSession.ReportLine(line); stoppedLine = JobSession.HasReportedLine; }
            }
            if (mJobPauseRequested && (((state == "Hold:0" || state == "Idle") && hasPosition) || (!IsGrbl11 && IsAnyHoldState(MachineStatus))))
            {
                if (!mJobStopped)
                {
                    mJobStopped = true;
                    mJobPausePosition = MachinePosition; mJobPauseWco = mWCO;
                    if (!stoppedLine) JobSession.ForgetReportedLine();
                }
                JobSession.SetState(JobExecutionState.Paused);
            }
            else if (state == "Hold:0" || (!IsGrbl11 && IsAnyHoldState(MachineStatus))) JobSession.SetState(JobExecutionState.Paused);
            else if (state == "Run" && !mJobPauseRequested) JobSession.SetState(JobExecutionState.Running);
            if (state == "Idle" && mJobAwaitIdle && !mJobPauseRequested && InProgram && mQueuePtr.Count == 0 && !HasPendingCommands())
            {
                mJobAwaitIdle = false;
                OnProgramEnd();
            }
        }

        private static bool SameJobPosition(GPoint left, GPoint right)
        {
            return !float.IsNaN(left.X) && !float.IsNaN(left.Y) && !float.IsNaN(left.Z) &&
                Math.Abs(left.X - right.X) <= .01 && Math.Abs(left.Y - right.Y) <= .01 && Math.Abs(left.Z - right.Z) <= .01;
        }
        private static string JobPointCommand(string command, GPoint point)
        { return string.Format(CultureInfo.InvariantCulture, "{0} X{1:0.###} Y{2:0.###} Z{3:0.###}", command, point.X, point.Y, point.Z); }

        public void ShowJobResumeDialog(Form owner)
        {
            if (JobSession == null || !JobSession.Active) { CycleStartResumeNative(); return; }
            if (!mJobStopped || mJobRebuild != null) return;
            using (JobResumeDialog dialog = new JobResumeDialog(JobSession, CanAdjustJob, HasStagedJobParameters))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                lock (this)
                {
                    if (!mJobStopped || !JobSession.Active || mJobRebuild != null) return;
                    if (dialog.Movements == 0 && mJobStagedDocument == null) { CycleStartResumeNative(); return; }
                    if (!CanAdjustJob) return;
                    StartJobRebuild(dialog.Movements);
                }
            }
        }

        private void CycleStartResumeNative()
        {
            if (!CanResumeHold) return;
            mJobPauseRequested = mJobStopped = false;
            mHoldByCoolingRequest = mHoldByUserRequest = false;
            if (JobSession != null && JobSession.Active) JobSession.SetState(JobExecutionState.Resuming);
            SendImmediate(126);
        }

        public void ShowJobParametersDialog(Form owner)
        {
            if (!CanAdjustJob) return;
            using (JobParametersDialog dialog = new JobParametersDialog(mJobStagedDocument ?? JobSession.Document, JobSession))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                lock (this)
                {
                    if (!CanAdjustJob) return;
                    try
                    {
                        LayeredCompilation compilation = CompileJobDocument(dialog.Document);
                        GrblFile prepared = new GrblFile();
                        if (!prepared.LoadCommandsWithProtection(compilation.OutputCommands, "Adjusted job", false, compilation.ExportRestricted)) throw new InvalidOperationException();
                        mJobStagedDocument = dialog.Document.Clone();
                        mJobError = null;
                    }
                    catch (Exception ex) { MessageBox.Show(owner, LayerText.Get("JobCompileFailed") + " " + ex.Message, LayerText.Get("JobParameters"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
            }
        }

        private LayeredCompilation CompileJobDocument(LaserDocument document)
        {
            LayeredCompilation compilation = LayeredJobCompiler.Compile(document, Math.Max(1, (int)Configuration.MaxPWM),
                Settings.GetObject("GrayScaleConversion.Gcode.LaserOptions.LaserOn", "M4"), "M5", JobSession == null ? 1 : JobSession.ObjectRepetitions);
            if (compilation.HasErrors) throw new InvalidOperationException(LayerText.Get("FixErrors"));
            return compilation;
        }

        private void StartJobRebuild(int movements)
        {
            if (!SameJobPosition(MachinePosition, mJobPausePosition) || !SameJobPosition(mWCO, mJobPauseWco))
            { mJobError = LayerText.Get("JobPositionChanged"); return; }
            int oldPosition = movements > 0 ? JobSession.RewindTarget(movements) : JobSession.CurrentIndex;
            bool inside = movements == 0 && JobSession.HasReportedLine;
            if (!JobSession.HasReportedLine) oldPosition = JobSession.PassStart;
            GrblCommand anchor = JobSession.Commands[Math.Min(oldPosition, JobSession.Commands.Count - 1)];
            LaserDocument document = (mJobStagedDocument ?? JobSession.Document).Clone();
            LayeredCompilation compilation;
            GrblFile prepared;
            try
            {
                compilation = CompileJobDocument(document);
                prepared = new GrblFile();
                if (!prepared.LoadCommandsWithProtection(compilation.OutputCommands, "Adjusted job", false, compilation.ExportRestricted)) throw new InvalidOperationException();
            }
            catch (Exception ex) { mJobError = LayerText.Get("JobCompileFailed") + " " + ex.Message; return; }
            List<GrblCommand> commands = prepared.ToList();
            int position = commands.FindIndex(c => c.SourceLayerId == anchor.SourceLayerId && c.SourceObjectIndex == anchor.SourceObjectIndex &&
                c.SourcePass == anchor.SourcePass && c.SourceCommandIndex == anchor.SourceCommandIndex);
            if (position < 0) { mJobError = LayerText.Get("JobCompileFailed"); return; }
            // Only a linear move can continue from its interior without changing arc geometry.
            anchor.BuildHelper();
            inside &= anchor.IsLinearMovement;
            anchor.DeleteHelper();
            mJobRebuild = new JobRebuild { File = prepared, Document = document, Compilation = compilation, Position = position,
                PositionBeforeReset = mJobPausePosition, WcoBeforeReset = mJobPauseWco, ContinueInsideLine = inside,
                Deadline = Tools.HiResTimer.TotalMilliseconds + 15000 };
            mJobError = null;
            JobSession.SetState(JobExecutionState.Resuming);
            InternalReset(true);
        }

        private void CommitJobRebuild()
        {
            JobRebuild rebuild = mJobRebuild;
            GrblCommand.StatePositionBuilder builder = new GrblCommand.StatePositionBuilder();
            List<GrblCommand> commands = rebuild.File.ToList();
            for (int i = 0; i < rebuild.Position; i++) builder.AnalyzeCommand(commands[i], false);
            // Do not arm the laser during the repositioning move.
            List<GrblCommand> prefix = new List<GrblCommand> { new GrblCommand("M5"), new GrblCommand("G21"), new GrblCommand("G90") };
            if (!rebuild.ContinueInsideLine)
                prefix.Add(new GrblCommand(string.Format(CultureInfo.InvariantCulture, "G0 {0} {1} {2}", builder.X, builder.Y, builder.Z)));
            prefix.Add(new GrblCommand(string.Format(CultureInfo.InvariantCulture, "{0} {1}", builder.F, builder.S)));
            prefix.Add(new GrblCommand(builder.GetSettledModals()));
            if (rebuild.Position < commands.Count)
            {
                GrblCommand first = commands[rebuild.Position];
                first.BuildHelper();
                if (first.IsMovement && first.G == null)
                {
                    GrblCommand explicitMotion = new GrblCommand(builder.MotionMode, first);
                    explicitMotion.SourceLayerId = first.SourceLayerId; explicitMotion.SourcePass = first.SourcePass;
                    explicitMotion.SourceObjectIndex = first.SourceObjectIndex;
                    explicitMotion.SourceCommandIndex = first.SourceCommandIndex; explicitMotion.SourceMovement = first.SourceMovement;
                    explicitMotion.IsCuttingMovement = first.IsCuttingMovement; explicitMotion.SetOffset(first.TimeOffset);
                    commands[rebuild.Position] = explicitMotion;
                }
                first.DeleteHelper();
            }
            // All compilation, parsing and reset validation succeeded before publishing either side.
            if (!file.LoadCommandsWithProtection(commands, "Layered project", false, rebuild.Compilation.ExportRestricted)) { FailJobRebuild(LayerText.Get("JobCompileFailed")); return; }
            LayerController.ApplyJobDocument(rebuild.Document);
            GrblFile preview = new GrblFile();
            preview.LoadCommandsWithProtection(rebuild.Compilation.PreviewCommands, "Layered preview", false, rebuild.Compilation.ExportRestricted); previewFile = preview;
            JobSession.ReplaceCommands(file, rebuild.Document, rebuild.Position);
            ClearQueue(false);
            foreach (GrblCommand command in prefix) mQueuePtr.Enqueue(command);
            foreach (GrblCommand command in JobSession.Commands.Skip(rebuild.Position)) mQueuePtr.Enqueue(PrepareJobCommand(command));
            mTP.Reset(false);
            mTP.JobContinue(file, rebuild.Position, prefix.Count);
            mJobRebuild = null; mJobStagedDocument = null;
            mJobPauseRequested = mJobStopped = mJobAwaitIdle = false;
            mHoldByUserRequest = mHoldByCoolingRequest = false;
            JobSession.SetState(JobExecutionState.Running);
            if (OnPreviewChanged != null) OnPreviewChanged();
        }
    }
}
