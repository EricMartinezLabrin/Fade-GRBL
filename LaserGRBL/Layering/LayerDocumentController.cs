using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Tools;

namespace LaserGRBL.Layering
{
    public partial class LayerDocumentController
    {
        private static readonly Color[] Palette = { Color.Red, Color.Blue, Color.Green, Color.Black, Color.Orange, Color.Purple, Color.Brown, Color.Cyan, Color.Magenta, Color.Gold, Color.DarkBlue, Color.DarkGreen };
        private readonly GrblCore core;
        private int requestedRevision;
        public CanvasClipboard Clipboard { get; private set; } = new CanvasClipboard();
        public IList<Guid> LastInsertedPaths { get; private set; } = new List<Guid>();

        public LaserDocument Document { get; private set; }
        public bool IsLayered { get { return Document != null; } }
        public bool IsCompilationCurrent { get; private set; } = true;
        public bool HasCompilationErrors { get; private set; }
        public event Action DocumentChanged;
        public event Action CompilationStateChanged;

        internal void ApplyJobDocument(LaserDocument document)
        {
            InvalidateCompilation();
            Document = document.Clone(); Document.IsDirty = true;
            transientEdit=false;
            History.Commit(Document, Selection);
            IsCompilationCurrent = true; HasCompilationErrors = false;
            Form mainForm = Application.OpenForms.Count == 0 ? null : FormsHelper.MainForm;
            if (mainForm != null)
                mainForm.BeginInvoke((MethodInvoker)(() => { NotifyDocumentChanged(); RaiseCompilationState(); }));
            else { NotifyDocumentChanged(); RaiseCompilationState(); }
        }

        public LayerDocumentController(GrblCore core) { this.core = core; }

        public void OpenLibraryDocument(LaserDocument document)
        {
            if (core.JobEditingLocked || !core.CanLoadNewFile) throw new InvalidOperationException(Community.CommunityText.Get("EditingLocked"));
            Community.CommunityRuntime.ValidateAccess(document);
            InvalidateCompilation(); Clipboard.Clear(); Document = document.Clone();
            Document.IsDirty = true; DocumentReplaced?.Invoke(); ResetHistory(false);
            NotifyDocumentChanged(); RequestCompile(true);
        }

        public void InsertLibraryDocument(LaserDocument document)
        {
            if (core.JobEditingLocked || !core.CanLoadNewFile) throw new InvalidOperationException(Community.CommunityText.Get("EditingLocked"));
            Community.CommunityRuntime.ValidateAccess(document);
            EnsureDocument();
            var clipboard = new CanvasClipboard(); clipboard.CopyLayers(document, document.Layers);
            InsertCopies(clipboard);
        }

        public void ClearCommunityHistory()
        {
            if (core.JobEditingLocked) throw new InvalidOperationException(Community.CommunityText.Get("EditingLocked"));
            if (!Community.CommunityRuntime.IsProtected(Document) && core.LoadedFile.ExportRestricted)
                core.LoadedFile.LoadCommands(new GrblCommand[0], "Community closed", false);
            Clipboard.Clear(); ResetHistory(Document == null || !Document.IsDirty);
        }

        public void MarkLibrarySaved()
        {
            if (Document == null) return;
            transientEdit = false; History.Commit(Document, Selection); History.MarkSaved();
            Document.IsDirty = false; NotifyDocumentChanged();
        }

        public void NewDocument()
        {
            if (core.JobEditingLocked) return;
            if (core.LoadedFile.ExportRestricted && !core.LoadedFile.CheckInUse(false))
                core.LoadedFile.LoadCommands(new GrblCommand[0], "New document", false);
            InvalidateCompilation();
            Clipboard.Clear();
            Document = new LaserDocument();
            DocumentReplaced?.Invoke();
            ResetHistory();
            IsCompilationCurrent = true; HasCompilationErrors = false;
            core.UseLegacyPreview();
            NotifyDocumentChanged();
        }

        public void ExitLayeredMode()
        {
            InvalidateCompilation();
            Clipboard.Clear();
            Document = null;
            DocumentReplaced?.Invoke();
            History = new CanvasHistory();
            IsCompilationCurrent = true;
            HasCompilationErrors = false;
            core.UseLegacyPreview();
            NotifyDocumentChanged();
        }

        public SvgImportResult ImportSvg(string filename, bool append)
        {
            if (core.JobEditingLocked) return new SvgImportResult();
            bool replace = !append || Document == null;
            if (replace) NewDocument();
            int speed = Settings.GetObject("GrayScaleConversion.VectorizeOptions.BorderSpeed", 1000);
            int configuredMax = Math.Max(1, (int)GrblCore.Configuration.MaxPWM);
            int rawPower = Settings.GetObject("GrayScaleConversion.Gcode.LaserOptions.PowerMax", configuredMax);
            SvgImportResult result = SvgLayerImporter.Import(filename, Document, speed, rawPower * 100.0 / configuredMax);
            FinishImport(replace);
            return result;
        }

        public void CaptureRasterImport(string filename, bool append, int previousCommandCount)
        {
            if (core.JobEditingLocked) return;
            int start = append ? Math.Min(previousCommandCount, core.LoadedFile.Commands.Count) : 0;
            List<string> commands = core.LoadedFile.Commands.Skip(start).Select(c => c.Command).ToList();
            if (commands.Count == 0) return;
            bool replace = !append || Document == null;
            if (replace) NewDocument();
            int maxPower = FindMaximumPower(commands);
            int minPower = FindMinimumPositivePower(commands);
            LaserLayer layer = new LaserLayer
            {
                Order = Document.Layers.Count,
                Name = Path.GetFileNameWithoutExtension(filename),
                Color = NextColor(),
                ContentKind = LayerContentKind.Raster,
                Mode = LayerMode.Image,
                SpeedMmPerMinute = FindFeed(commands),
                PowerPercent = maxPower <= 0 ? 100 : Math.Min(100, maxPower * 100.0 / Math.Max(1.0, (double)GrblCore.Configuration.MaxPWM)),
                RasterItem = new RasterItem
                {
                    SourceName = Path.GetFileName(filename),
                    ImageData = File.Exists(filename) ? File.ReadAllBytes(filename) : null,
                    BaseCommands = commands,
                    OriginalMaxPower = Math.Max(1, maxPower),
                    OriginalMinPower = minPower,
                    MinPowerPercent = minPower <= 0 ? 0 : Math.Min(100, minPower * 100.0 / Math.Max(1.0, (double)GrblCore.Configuration.MaxPWM)),
                    Settings = CaptureLatestRasterSettings()
                }
            };
            Document.Layers.Add(layer);
            Document.NormalizeOrder();
            FinishImport(replace);
        }

        public void Load(string filename)
        {
            if (core.JobEditingLocked) return;
            LaserDocument loaded = LayerProjectSerializer.Load(filename);
            Clipboard.Clear();
            Document = loaded;
            DocumentReplaced?.Invoke();
            ResetHistory();
            NotifyDocumentChanged();
            var missingFonts = Document.Layers.SelectMany(l => l.VectorItems).Where(i => i.Text != null && !CanvasObjects.FontAvailable(i.Text.FontFamily)).Select(i => i.Text.FontFamily).Distinct().ToList();
            if (missingFonts.Count > 0 && Application.OpenForms.Count > 0)
                MessageBox.Show(string.Format(LayerText.Get("MissingFont"), string.Join(", ", missingFonts.ToArray())), LayerText.Get("TextTool"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            RequestCompile(true);
        }

        public void Save(string filename)
        {
            // Validation can commit a cell immediately before Save, ahead of its
            // queued Changed notification. Include that edit in the saved revision.
            transientEdit=false;
            History.Commit(Document, Selection);
            LayerProjectSerializer.Save(filename, Document); History.MarkSaved(); NotifyDocumentChanged();
        }

        public bool SetPassOrder(JobPassOrder order, int objectRepetitions = 1)
        {
            if (Document == null || core.JobEditingLocked) return false;
            LaserDocument snapshot = Document.Clone(); snapshot.PassOrder = order;
            LayeredCompilation compilation = LayeredJobCompiler.Compile(snapshot, Math.Max(1, (int)GrblCore.Configuration.MaxPWM),
                Settings.GetObject("GrayScaleConversion.Gcode.LaserOptions.LaserOn", "M4"), "M5", objectRepetitions);
            if (compilation.HasErrors || !core.ApplyLayeredCompilation(compilation)) return false;
            InvalidateCompilation();
            Document.IsDirty |= Document.PassOrder != order; Document.PassOrder = order;
            transientEdit=false;
            History.Commit(Document, Selection);
            IsCompilationCurrent = true; HasCompilationErrors = false;
            NotifyDocumentChanged(); RaiseCompilationState();
            return true;
        }

        public bool ReprocessRaster(LaserLayer layer)
        {
            if (core.JobEditingLocked) return false;
            if (layer == null || layer.ContentKind != LayerContentKind.Raster || layer.RasterItem == null || layer.RasterItem.ImageData == null) return false;
            foreach (RasterSetting setting in layer.RasterItem.Settings)
                Settings.SetObject(setting.Key, setting.ToValue());
            string extension = Path.GetExtension(layer.RasterItem.SourceName);
            if (string.IsNullOrEmpty(extension)) extension = ".png";
            string temporary = Path.Combine(Path.GetTempPath(), "lasergrbl-layer-" + Guid.NewGuid().ToString("N") + extension);
            int beforeCommands = core.LoadedFile.Commands.Count;
            int beforeSettings = Project.ProjectSettings.Count;
            try
            {
                File.WriteAllBytes(temporary, layer.RasterItem.ImageData);
                RasterConverter.RasterToLaserForm.CreateAndShowDialog(core, temporary, true);
                List<string> generated = core.LoadedFile.Commands.Skip(beforeCommands).Select(command => command.Command).ToList();
                if (generated.Count == 0) return false;
                int maxPower = FindMaximumPower(generated);
                int minPower = FindMinimumPositivePower(generated);
                layer.RasterItem.BaseCommands = generated;
                layer.RasterItem.OriginalMaxPower = Math.Max(1, maxPower);
                layer.RasterItem.OriginalMinPower = minPower;
                layer.RasterItem.MinPowerPercent = minPower <= 0 ? 0 : Math.Min(100, minPower * 100.0 / Math.Max(1.0, (double)GrblCore.Configuration.MaxPWM));
                if (Project.ProjectSettings.Count > beforeSettings) layer.RasterItem.Settings = CaptureLatestRasterSettings();
                layer.SpeedMmPerMinute = FindFeed(generated);
                layer.PowerPercent = maxPower <= 0 ? layer.PowerPercent : Math.Min(100, maxPower * 100.0 / Math.Max(1.0, (double)GrblCore.Configuration.MaxPWM));
                Changed();
                return true;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        public LaserLayer AddLayer()
        {
            if (core.JobEditingLocked) return null;
            EnsureDocument();
            LaserLayer layer = new LaserLayer { Order = Document.Layers.Count, Name = "Layer " + (Document.Layers.Count + 1), Color = NextColor() };
            Document.Layers.Add(layer);
            Changed();
            return layer;
        }

        public LaserLayer Duplicate(LaserLayer source) { return DuplicateLayers(new[] { source }).FirstOrDefault(); }
        public void CopyLayers(IEnumerable<LaserLayer> layers) { Clipboard.CopyLayers(Document, layers); }
        public void CopyObjects(IEnumerable<VectorSelection> objects, IEnumerable<LaserLayer> rasters = null) { Clipboard.CopyObjects(Document, objects, rasters); }
        public IList<LaserLayer> Paste() { return InsertCopies(Clipboard); }
        public IList<LaserLayer> DuplicateLayers(IEnumerable<LaserLayer> layers)
        {
            CanvasClipboard temporary = new CanvasClipboard(); temporary.CopyLayers(Document, layers);
            return InsertCopies(temporary);
        }
        public IList<LaserLayer> DuplicateObjects(IEnumerable<VectorSelection> objects, IEnumerable<LaserLayer> rasters = null)
        {
            CanvasClipboard temporary = new CanvasClipboard(); temporary.CopyObjects(Document, objects, rasters);
            return InsertCopies(temporary);
        }
        private IList<LaserLayer> InsertCopies(CanvasClipboard clipboard)
        {
            IList<LaserLayer> copies = clipboard.Paste(Document, core.JobEditingLocked);
            LastInsertedPaths = clipboard.LastPastedPaths.ToList();
            if (copies.Count > 0) { Changed(false); RequestCompile(false, false); }
            return copies.Select(c => Document.Layers.FirstOrDefault(l => l.Id == c.Id) ?? Document.FindByColor(c.Color, c.ContentKind)).Where(l => l != null).Distinct().ToList();
        }

        public void Remove(LaserLayer layer) { RemoveLayers(new[] { layer }); }
        public void RemoveLayers(IEnumerable<LaserLayer> layers)
        {
            if (Document == null || core.JobEditingLocked || layers == null) return;
            HashSet<LaserLayer> selected = new HashSet<LaserLayer>(layers);
            if (Document.Layers.RemoveAll(layer => selected.Contains(layer)) > 0) Changed();
        }
        public void Move(LaserLayer layer, int delta)
        {
            if (core.JobEditingLocked || Document == null || layer == null) return;
            int oldIndex = Document.Layers.IndexOf(layer), newIndex = Math.Max(0, Math.Min(Document.Layers.Count - 1, oldIndex + delta));
            if (oldIndex < 0 || oldIndex == newIndex) return;
            Document.Layers.RemoveAt(oldIndex); Document.Layers.Insert(newIndex, layer); Changed();
        }

        public void MoveTo(LaserLayer layer, int newIndex)
        {
            if (core.JobEditingLocked || Document == null || layer == null) return;
            int oldIndex = Document.Layers.IndexOf(layer);
            newIndex = Math.Max(0, Math.Min(Document.Layers.Count - 1, newIndex));
            if (oldIndex < 0 || oldIndex == newIndex) return;
            Document.Layers.RemoveAt(oldIndex);
            Document.Layers.Insert(newIndex, layer);
            Changed();
        }

        private CancellationTokenSource compilationCancellation = new CancellationTokenSource();
        private int InvalidateCompilation()
        {
            var previous=Interlocked.Exchange(ref compilationCancellation,new CancellationTokenSource());
            previous.Cancel();previous.Dispose();return Interlocked.Increment(ref requestedRevision);
        }
        public void PreviewChanged()
        {
            if (core.JobEditingLocked) return;
            InvalidateCompilation(); IsCompilationCurrent = false;
            VisualChanged?.Invoke(); RaiseCompilationState();
        }
        public event Action VisualChanged;

        public void Changed(bool compile = true)
        {
            if (core.JobEditingLocked) return;
            EnsureDocument();
            CanvasGroups.MergeColors(Document);
            transientEdit=false;
            History.Commit(Document, Selection);
            NotifyDocumentChanged();
            if (compile) RequestCompile();
        }

        public void RequestCompile(bool immediate = false, bool notifyDocument = true)
        {
            if (core.JobEditingLocked) return;
            if (Document == null) return;
            int revision = InvalidateCompilation();
            var cancellation=compilationCancellation.Token;
            LaserDocument snapshot = Document.Clone();
            IsCompilationCurrent = false;
            RaiseCompilationState();
            Form compilationOwner = Application.OpenForms.Count == 0 ? null : FormsHelper.MainForm;
            if (compilationOwner == null || !compilationOwner.IsHandleCreated || compilationOwner.IsDisposed) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (!immediate) Thread.Sleep(350);
                if (revision != requestedRevision) return;
                int maxPwm = Math.Max(1, (int)GrblCore.Configuration.MaxPWM);
                string laserOn = Settings.GetObject("GrayScaleConversion.Gcode.LaserOptions.LaserOn", "M4");
                LayeredCompilation compilation;
                try { compilation = LayeredJobCompiler.Compile(snapshot, maxPwm, laserOn, "M5", 1, cancellation); }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    compilation = new LayeredCompilation { HasErrors = true };
                    foreach (LaserLayer layer in snapshot.Layers.Where(layer => layer.OutputEnabled))
                    {
                        layer.HasError = true;
                        layer.StatusMessage = ex.Message;
                    }
                }
                if (revision != requestedRevision) return;
                if (compilationOwner.IsDisposed || !compilationOwner.IsHandleCreated) return;
                try { compilationOwner.BeginInvoke((MethodInvoker)(() =>
                {
                    if (revision != requestedRevision) return;
                    foreach (LaserLayer compiledLayer in snapshot.Layers)
                    {
                        LaserLayer liveLayer = Document.Layers.FirstOrDefault(layer => layer.Id == compiledLayer.Id);
                        if (liveLayer != null)
                        {
                            liveLayer.StatusMessage = compiledLayer.StatusMessage;
                            liveLayer.HasError = compiledLayer.HasError;
                        }
                    }
                    if (!core.ApplyLayeredCompilation(compilation, notifyDocument))
                    {
                        IsCompilationCurrent = false;
                        RaiseCompilationState();
                        return;
                    }
                    IsCompilationCurrent = true;
                    HasCompilationErrors = compilation.HasErrors;
                    if (notifyDocument) NotifyDocumentChanged();
                    RaiseCompilationState();
                })); }
                catch (InvalidOperationException) { /* The owning window closed while compilation completed. */ }
            });
        }

        private void EnsureDocument() { if (Document == null) { Document = new LaserDocument(); ResetHistory(); } }
        private void NotifyDocumentChanged() { VisualChanged?.Invoke(); DocumentChanged?.Invoke(); }
        private void RaiseCompilationState() { CompilationStateChanged?.Invoke(); }
        private Color NextColor()
        {
            HashSet<int> used = Document == null ? new HashSet<int>() : new HashSet<int>(Document.Layers.Select(l => l.Color.ToArgb()));
            foreach (Color color in Palette) if (!used.Contains(color.ToArgb())) return color;
            for (int i = 0; i < 360; i += 17)
            {
                Color color = FromHsv(i, .85, .85);
                if (!used.Contains(color.ToArgb())) return color;
            }
            return Color.Gray;
        }

        private static Color FromHsv(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60), v = value * 255, p = v * (1 - saturation), q = v * (1 - f * saturation), t = v * (1 - (1 - f) * saturation);
            switch (hi) { case 0: return Color.FromArgb(255, (int)v, (int)t, (int)p); case 1: return Color.FromArgb(255, (int)q, (int)v, (int)p); case 2: return Color.FromArgb(255, (int)p, (int)v, (int)t); case 3: return Color.FromArgb(255, (int)p, (int)q, (int)v); case 4: return Color.FromArgb(255, (int)t, (int)p, (int)v); default: return Color.FromArgb(255, (int)v, (int)p, (int)q); }
        }

        private static int FindMaximumPower(IEnumerable<string> commands)
        {
            int maximum = 0;
            Regex regex = new Regex(@"(?<![A-Z])S(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            foreach (string line in commands) foreach (Match match in regex.Matches(line)) { double n; if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out n)) maximum = Math.Max(maximum, (int)Math.Round(n)); }
            return maximum;
        }

        private static int FindMinimumPositivePower(IEnumerable<string> commands)
        {
            int minimum = int.MaxValue;
            Regex regex = new Regex(@"(?<![A-Z])S(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            foreach (string line in commands)
                foreach (Match match in regex.Matches(line))
                {
                    double value;
                    if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0)
                        minimum = Math.Min(minimum, (int)Math.Round(value));
                }
            return minimum == int.MaxValue ? 0 : minimum;
        }

        private static int FindFeed(IEnumerable<string> commands)
        {
            Regex regex = new Regex(@"(?<![A-Z])F(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            foreach (string line in commands) { Match match = regex.Match(line); double n; if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return Math.Max(1, (int)Math.Round(n)); }
            return 1000;
        }

        private static List<RasterSetting> CaptureLatestRasterSettings()
        {
            Dictionary<string, object> values = Project.ProjectSettings.LastOrDefault();
            if (values == null) return new List<RasterSetting>();
            return values.Where(pair => pair.Key != "ImageName" && pair.Key != "ImageBase64")
                .Select(pair => RasterSetting.From(pair.Key, pair.Value)).Where(setting => setting != null).ToList();
        }
    }
}
