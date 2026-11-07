using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LaserGRBL.Layering;

namespace LaserGRBL.UserControls
{
    public partial class GrblPanel
    {
        private readonly CanvasViewport viewport = new CanvasViewport();
        private readonly Timer panTimer = new Timer { Interval = 40 };
        private Point panPoint;
        private Cursor cursorBeforePan;
        private bool panning, panDirty;
        private bool awaitingDocumentPreview;
        private RectangleF? selectionRectangle, referenceBounds;
        private bool selectionCrossing;
        public event Action ViewportChanged;
        private void InitializeNavigation()
        {
            MouseDown += NavigationMouseDown; MouseMove += NavigationMouseMove;
            MouseUp += (s, e) => { if (e.Button == MouseButtons.Middle) EndPan(); };
            MouseCaptureChanged += (s, e) => { if (!Capture) EndPan(); };
            LostFocus += (s, e) => EndPan();
            panTimer.Tick += (s, e) => { if (panDirty) { panDirty = false; RecreateBMP(); } };
            Disposed += (s, e) => panTimer.Dispose();
        }
        private Matrix EmptyViewport()
        { return new Matrix(EmptyEditorScale, 0, 0, -EmptyEditorScale, 20, ClientSize.Height - 20); }
        private void NavigationMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Middle) return;
            Focus(); panning = true; panPoint = e.Location; cursorBeforePan = Cursor; Capture = true; Cursor = Cursors.SizeAll; panTimer.Start();
        }
        private void NavigationMouseMove(object sender, MouseEventArgs e)
        {
            if (!panning) return;
            using (var fallback = UseEmptyEditorViewport ? EmptyViewport() : mLastMatrix == null ? new Matrix() : mLastMatrix.Clone())
                viewport.Pan(e.X-panPoint.X, e.Y-panPoint.Y, fallback);
            panPoint = e.Location; panDirty = true; ViewportChanged?.Invoke(); Invalidate();
        }
        private void EndPan()
        {
            if (!panning) return;
            panning = false; Capture = false; Cursor = cursorBeforePan ?? Cursors.Default; panTimer.Stop();
            if (panDirty) { panDirty = false; RecreateBMP(); }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (panning && keyData == Keys.Escape) { EndPan(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        public void ResetViewport()
        {
            EndPan(); viewport.Reset(); selectionRectangle = referenceBounds = null;
            awaitingDocumentPreview = true;
            Invalidate();
            ViewportChanged?.Invoke();
        }
        public void SetSelectionRectangle(RectangleF? rectangle, bool crossing) { selectionRectangle = rectangle; selectionCrossing = crossing; Invalidate(); }
        public void SetReferenceBounds(RectangleF? bounds) { referenceBounds = bounds; Invalidate(); }
        private void DrawCanvasOverlay(Graphics graphics)
        {
            RectangleF? screenReference = null;
            if (referenceBounds.HasValue)
            {
                var r = referenceBounds.Value;
                screenReference = CanvasLayout.SelectionRectangle(MachineToClient(new PointF(r.Left, r.Top)), MachineToClient(new PointF(r.Right, r.Bottom)));
            }
            CanvasOverlay.Draw(graphics, selectionRectangle, selectionCrossing, screenReference);
        }
    }
}
