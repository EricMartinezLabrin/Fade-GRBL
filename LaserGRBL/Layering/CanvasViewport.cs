using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LaserGRBL.Layering
{
    // View state is independent of document geometry and undo history.
    public sealed class CanvasViewport
    {
        private readonly object sync = new object();
        private float[] matrix;
        private int revision;
        public static LaserPoint PanDelta(double worldWidth, double worldHeight, int pixelWidth, int pixelHeight, Point delta)
        { return new LaserPoint(delta.X * worldWidth / Math.Max(1, pixelWidth), delta.Y * worldHeight / Math.Max(1, pixelHeight)); }
        public int Revision { get { lock (sync) return revision; } }
        public Matrix Snapshot() { lock (sync) return matrix == null ? null : new Matrix(matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5]); }
        public void Reset() { lock (sync) { matrix = null; revision++; } }
        public bool Accept(Matrix rendered, int expectedRevision, bool freeze)
        {
            lock (sync)
            {
                if (revision != expectedRevision) return false;
                if (freeze && matrix == null) matrix = rendered.Elements;
                return true;
            }
        }
        public void Pan(float dx, float dy, Matrix fallback)
        {
            lock (sync)
            {
                if (matrix == null) matrix = fallback.Elements;
                matrix[4] += dx; matrix[5] += dy; revision++;
            }
        }
        public bool TryTransform(PointF point, bool inverse, out PointF result)
        {
            lock (sync)
            {
                result = point; if (matrix == null) return false;
                if (!inverse) result = new PointF(point.X*matrix[0] + point.Y*matrix[2] + matrix[4], point.X*matrix[1] + point.Y*matrix[3] + matrix[5]);
                else
                {
                    double determinant = matrix[0]*matrix[3] - matrix[1]*matrix[2]; if (Math.Abs(determinant) < 1e-12) return false;
                    double x = point.X-matrix[4], y = point.Y-matrix[5];
                    result = new PointF((float)((x*matrix[3]-y*matrix[2])/determinant), (float)((y*matrix[0]-x*matrix[1])/determinant));
                }
                return true;
            }
        }
    }
}
