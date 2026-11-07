//Copyright (c) 2016-2021 Diego Settimi - https://github.com/arkypita/

// This program is free software; you can redistribute it and/or modify  it under the terms of the GPLv3 General Public License as published by  the Free Software Foundation; either version 3 of the License, or (at  your option) any later version.
// This program is distributed in the hope that it will be useful, but  WITHOUT ANY WARRANTY; without even the implied warranty of  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GPLv3  General Public License for more details.
// You should have received a copy of the GPLv3 General Public License  along with this program; if not, write to the Free Software  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307,  USA. using System;

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LaserGRBL;
using SharpGL;

namespace LaserGRBL.UserControls
{
	public partial class GrblPanel : UserControl, IGrblPanel
	{
		GrblCore Core;
		System.Drawing.Bitmap mBitmap;
		System.Threading.Thread TH;
		Matrix mLastMatrix;
		private GPoint mLastWPos;
		private GPoint mLastMPos;
		private float mCurF;
		private float mCurS;
		private bool mFSTrig;
		private RectangleF? mSelectionBounds;
		private Layering.VectorPath[] mSelectionContours = new Layering.VectorPath[0];
		private bool mLayerEditing;

		public GrblPanel()
		{
			InitializeComponent();
            InitializeNavigation();
			Settings.CurrentGraphicMode = Settings.GraphicMode.GDI;
			GrblPanel3D.CurrentVendor = "LaserGRBL";
			GrblPanel3D.CurrentRenderer = "Legacy Preview";
			Logger.LogMessage("OpenGL", "{0}->{1} {2}, {3}", Settings.RequestedGraphicMode, Settings.CurrentGraphicMode, GrblPanel3D.CurrentVendor, GrblPanel3D.CurrentRenderer);

			SetStyle(ControlStyles.UserPaint, true);
			SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
			SetStyle(ControlStyles.AllPaintingInWmPaint, true);
			SetStyle(ControlStyles.ResizeRedraw, true);
			mLastWPos = GPoint.Zero;
			mLastMPos = GPoint.Zero;

			forcez = Settings.GetObject("Enale Z Jog Control", false);
			SettingsForm.SettingsChanged += SettingsForm_SettingsChanged;
		}

		private void SettingsForm_SettingsChanged(object sender, EventArgs e)
		{
			bool newforce = Settings.GetObject("Enale Z Jog Control", false);
			if (newforce != forcez)
			{
				forcez = newforce;
				Invalidate();
			}
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			e.Graphics.Clear(ColorScheme.PreviewBackColor);
		}

		bool forcez = false;
        private readonly Layering.CanvasScene editableScene = new Layering.CanvasScene();
		protected override void OnPaint(PaintEventArgs e)
		{
            if(mLayerEditing && Core != null && Core.LayerController.IsLayered && !Core.JobEditingLocked)
            {
                editableScene.Draw(e.Graphics,Core.LayerController.Document,MachineToClient,ClientToMachine,ClientSize);
                DrawSelection(e.Graphics); DrawCanvasOverlay(e.Graphics); base.OnPaint(e); return;
            }

			try
			{
				if (mBitmap != null)
					e.Graphics.DrawImage(mBitmap, 0, 0, Width, Height);

				if (Core != null)
				{
					PointF p = MachineToDraw(mLastWPos.ToPointF());
					e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
					e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

					using (Pen px = GetPen(ColorScheme.PreviewCross, 2f))
					{
						e.Graphics.DrawLine(px, (int)p.X, (int)p.Y - 5, (int)p.X, (int)p.Y - 5 + 10);
						e.Graphics.DrawLine(px, (int)p.X - 5, (int)p.Y, (int)p.X - 5 + 10, (int)p.Y);
					}

					using (Brush b = GetBrush(ColorScheme.PreviewText))
					{
						Rectangle r = ClientRectangle;
						r.Inflate(-5, -5);
						StringFormat sf = new StringFormat();

						//  II | I
						// ---------
						// III | IV
						GrblFile.CartesianQuadrant q = Core != null && Core.PreviewFile != null ? Core.PreviewFile.Quadrant : GrblFile.CartesianQuadrant.Unknown;
						sf.Alignment = q == GrblFile.CartesianQuadrant.II || q == GrblFile.CartesianQuadrant.III ? StringAlignment.Near : StringAlignment.Far;
						sf.LineAlignment = q == GrblFile.CartesianQuadrant.III || q == GrblFile.CartesianQuadrant.IV ? StringAlignment.Far : StringAlignment.Near;

						String position = string.Format("X: {0:0.000} Y: {1:0.000}", Core != null ? mLastMPos.X : 0, Core != null ? mLastMPos.Y : 0);

                        if (Core != null && (mLastWPos.Z != 0 || mLastMPos.Z != 0 || forcez))
                            position = position + string.Format(" Z: {0:0.000}", mLastMPos.Z);

                        if (Core != null && Core.WorkingOffset != GPoint.Zero)
							position = position + "\n" + string.Format("X: {0:0.000} Y: {1:0.000}", Core != null ? mLastWPos.X : 0, Core != null ? mLastWPos.Y : 0);

                        if (Core != null && Core.WorkingOffset != GPoint.Zero  && (mLastWPos.Z != 0 || mLastMPos.Z != 0 || forcez))
                            position = position + string.Format(" Z: {0:0.000}", mLastWPos.Z);

                        if (mCurF != 0 || mCurS != 0 || mFSTrig)
						{
							mFSTrig = true;
							String fs = string.Format("F: {0:00000.##} S: {1:000.##}", Core != null ? mCurF : 0, Core != null ? mCurS : 0);
							position = position + "\n" + fs;
						}

						e.Graphics.DrawString(position, Font, b, r, sf);
					}
				}

				DrawSelection(e.Graphics);
                DrawCanvasOverlay(e.Graphics);
			}
			catch (Exception ex)
			{
				Logger.LogException("GrblPanel Paint", ex);
			}
		}


		private Pen GetPen(Color color, float width)
		{ return new Pen(color, width); }

		private Pen GetPen(Color color)
		{ return new Pen(color); }

		private Brush GetBrush(Color color)
		{ return new SolidBrush(color); }

		public void SetCore(GrblCore core)
		{
			Core = core;
            Core.LayerController.VisualChanged += Invalidate;
            Disposed += (s,e) => { Core.LayerController.VisualChanged -= Invalidate; editableScene.Dispose(); };
			Core.OnFileLoading += OnFileLoading;
			Core.OnFileLoaded += OnFileLoaded;
			Core.OnPreviewChanged += RecreateBMP;
            Core.OnAutoSizeDrawing += c => { ResetViewport(); awaitingDocumentPreview = false; RecreateBMP(); };
		}

		void OnFileLoading(long elapsed, string filename)
		{
			AbortCreation();
		}

		void OnFileLoaded(long elapsed, string filename)
		{
            awaitingDocumentPreview = false;
			RecreateBMP();
		}

		public void RecreateBMP()
		{
			AbortCreation();

			TH = new System.Threading.Thread(DoTheWork);
			TH.Name = "GrblPanel Drawing Thread";
			TH.Start();
		}

		private void AbortCreation()
		{
			if (TH != null)
			{
				TH.Abort();
				TH = null;
			}
		}

		protected override void OnSizeChanged(EventArgs e)
		{
			base.OnSizeChanged(e);
			RecreateBMP();
		}

		private void DoTheWork()
		{
			try
			{
				Size wSize = Size;

				if (wSize.Width < 1 || wSize.Height < 1)
					return;

				System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(wSize.Width, wSize.Height);
                int viewRevision = viewport.Revision;
				using (System.Drawing.Graphics g = Graphics.FromImage(bmp))
				{
					g.SmoothingMode = SmoothingMode.AntiAlias;
					g.InterpolationMode = InterpolationMode.HighQualityBicubic;
					g.PixelOffsetMode = PixelOffsetMode.HighQuality;
					g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                    using (var view = viewport.Snapshot() ?? (mLayerEditing && (Core == null || Core.PreviewFile.Commands.Count == 0) ? EmptyViewport() : null))
                    {
                        if (Core != null) Core.PreviewFile.DrawOnGraphics(g, wSize, view);
                        else if (view != null) g.Transform = view;
                    }
                    using (var rendered = g.Transform)
                    {
                        if (!viewport.Accept(rendered, viewRevision, mLayerEditing && !awaitingDocumentPreview)) { bmp.Dispose(); return; }
                        mLastMatrix = rendered.Clone();
                    }
				}

				AssignBMP(bmp);
			}
			catch (System.Threading.ThreadAbortException)
			{
 				//standard condition for abort and recreation
			}
			catch (Exception ex)
			{
				Logger.LogException("Drawing Preview", ex);
			}
		}

		public PointF MachineToDraw(PointF p)
		{
            PointF transformed; if (viewport.TryTransform(p, false, out transformed)) return transformed;
			if (mLastMatrix == null)
				return p;

			PointF[] pa = new PointF[] { p };
			mLastMatrix.TransformPoints(pa);
			return pa[0];
		}

		public PointF DrawToMachine(PointF p)
		{
            PointF transformed; if (viewport.TryTransform(p, true, out transformed)) return transformed;
			if (mLastMatrix == null || !mLastMatrix.IsInvertible)
				return p;

			Matrix mInvert = mLastMatrix.Clone();
			mInvert.Invert();

			PointF[] pa = new PointF[] { p };
			mInvert.TransformPoints(pa);

			return pa[0];
		}

        // A new drawing needs a metric viewport before its first G-code render.
        // Keep this fallback confined to editing; legacy machine jogging is unchanged.
        private float EmptyEditorScale { get { return Math.Max(1, Math.Min(ClientSize.Width - 40, ClientSize.Height - 40)) / 100f; } }
        private bool UseEmptyEditorViewport { get { return mLayerEditing && (mLastMatrix == null || (mLastMatrix.IsIdentity && (Core == null || Core.PreviewFile.Commands.Count == 0))); } }
        public PointF MachineToClient(PointF point)
        {
            PointF transformed; if (viewport.TryTransform(point, false, out transformed)) return transformed;
            if (UseEmptyEditorViewport) return new PointF(20 + point.X * EmptyEditorScale, ClientSize.Height - 20 - point.Y * EmptyEditorScale);
            return MachineToDraw(point);
        }
        public PointF ClientToMachine(PointF point)
        {
            PointF transformed; if (viewport.TryTransform(point, true, out transformed)) return transformed;
            if (UseEmptyEditorViewport) return new PointF((point.X - 20) / EmptyEditorScale, (ClientSize.Height - 20 - point.Y) / EmptyEditorScale);
            return DrawToMachine(point);
        }
		public void SetSelectionBounds(RectangleF? bounds) { mSelectionBounds = bounds; Invalidate(); }
		public void SetSelectionContours(System.Collections.Generic.IEnumerable<Layering.VectorPath> paths) { mSelectionContours = System.Linq.Enumerable.ToArray(paths); Invalidate(); }
		public void SetLayerEditing(bool enabled) { mLayerEditing = enabled; }

		private void DrawSelection(Graphics graphics)
		{
			Layering.LayerGeometry.DrawContours(graphics, mSelectionContours, MachineToClient);
			if (!mSelectionBounds.HasValue) return;
			RectangleF bounds = mSelectionBounds.Value;
            PointF first = MachineToClient(new PointF(bounds.Left, bounds.Top));
            PointF second = MachineToClient(new PointF(bounds.Right, bounds.Bottom));
			RectangleF screen = RectangleF.FromLTRB(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));
			Layering.LayerGeometry.DrawSelectionBounds(graphics, screen);
		}

		private void AssignBMP(System.Drawing.Bitmap bmp)
		{
			lock (this)
			{
				if (mBitmap != null)
					mBitmap.Dispose();

				mBitmap = bmp;
			}
			Invalidate();
		}

		public void TimerUpdate()
		{
			if (Core != null && (mLastWPos != Core.WorkPosition || mLastMPos != Core.MachinePosition || mCurF != Core.CurrentF || mCurS != Core.CurrentS))
			{
				mLastWPos = Core.WorkPosition;
				mLastMPos = Core.MachinePosition;
				mCurF = Core.CurrentF;
				mCurS = Core.CurrentS;
				Invalidate();
			}
		}


		public void OnColorChange()
		{
			RecreateBMP();
		}

		private void GrblPanel_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			if (!mLayerEditing && (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right) && Settings.GetObject("Click N Jog", true))
			{
				PointF coord = DrawToMachine(new PointF(e.X, e.Y));
				Core.JogToPosition(coord, e.Button == MouseButtons.Right);
			}
		}
	}
}
