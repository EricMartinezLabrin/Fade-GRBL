using System;
using System.Collections.Generic;
using System.Linq;
using ClipperLib;

namespace LaserGRBL.Layering
{
    internal static class ObjectCutOrder
    {
        // Only strict containment affects depth; crossing and touching boundaries are not interior cuts.
        public static IList<T> InsideFirst<T>(IList<T> objects, Func<T, VectorPath> getPath)
        {
            var paths = objects.Select(getPath).Select(path => path == null ? null :
                path.Points.Select(p => new IntPoint((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000))).ToList()).ToList();
            var closed = objects.Select(getPath).Select(path => path != null && path.Closed).ToList();
            var depth = new int[objects.Count];
            for (int outer = 0; outer < objects.Count; outer++)
            {
                var polygon = paths[outer];
                if (!closed[outer] || polygon.Count < 3 || Math.Abs(Clipper.Area(polygon)) < 1) continue;
                long left = polygon.Min(p => p.X), right = polygon.Max(p => p.X), bottom = polygon.Min(p => p.Y), top = polygon.Max(p => p.Y);
                for (int inner = 0; inner < objects.Count; inner++)
                {
                    var candidate = paths[inner];
                    if (outer == inner || candidate == null || candidate.Count < 2) continue;
                    if (candidate.Any(p => p.X <= left || p.X >= right || p.Y <= bottom || p.Y >= top)) continue;
                    if (candidate.Any(p => Clipper.PointInPolygon(p, polygon) != 1)) continue;
                    bool crosses = false;
                    int segments = closed[inner] ? candidate.Count : candidate.Count - 1;
                    for (int i = 0; i < segments && !crosses; i++)
                        for (int j = 0; j < polygon.Count && !crosses; j++)
                            crosses = Intersects(candidate[i], candidate[(i + 1) % candidate.Count], polygon[j], polygon[(j + 1) % polygon.Count]);
                    if (!crosses) depth[inner]++;
                }
            }
            return objects.Select((value, index) => new { Value = value, Depth = depth[index] }).OrderByDescending(item => item.Depth).Select(item => item.Value).ToList();
        }

        private static double Cross(IntPoint a, IntPoint b, IntPoint c)
        { return ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X); }

        private static bool Intersects(IntPoint a, IntPoint b, IntPoint c, IntPoint d)
        {
            if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X) ||
                Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y)) return false;
            double abC = Cross(a, b, c), abD = Cross(a, b, d), cdA = Cross(c, d, a), cdB = Cross(c, d, b);
            return Math.Sign(abC) * Math.Sign(abD) <= 0 && Math.Sign(cdA) * Math.Sign(cdB) <= 0;
        }
    }
}
