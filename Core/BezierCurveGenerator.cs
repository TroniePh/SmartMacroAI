using System.Drawing;

namespace SmartMacroAI.Core;

/// <summary>
/// Builds randomized cubic (4-point) or quintic (6-point) Bézier screen paths with
/// ease-in-out parameterization along the curve (smoothstep on the curve parameter).
/// </summary>
public static class BezierCurveGenerator
{
    /// <summary>Minimum interpolated samples for any segment (inclusive).</summary>
    public const int MinSteps = 20;

    /// <summary>Maximum interpolated samples for any segment (inclusive).</summary>
    public const int MaxSteps = 200;

    /// <summary>Maps linear progress in [0,1] to a smooth ease-in-out in [0,1].</summary>
    public static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Maps distance in pixels to a step count in [<see cref="MinSteps"/>, <see cref="MaxSteps"/>].</summary>
    public static int ComputeStepCount(double distancePixels)
    {
        if (double.IsNaN(distancePixels) || double.IsInfinity(distancePixels))
            return MinSteps;
        double d = Math.Max(0, distancePixels);
        // Scale so short moves stay near MinSteps and long moves approach MaxSteps.
        int steps = (int)Math.Round(20 + d / 6.0);
        return Math.Clamp(steps, MinSteps, MaxSteps);
    }

    /// <summary>
    /// Samples a randomized Bézier curve from <paramref name="start"/> to <paramref name="end"/>.
    /// The first point equals <paramref name="start"/> and the last equals <paramref name="end"/> (within float precision).
    /// </summary>
    /// <param name="start">Curve start (screen space).</param>
    /// <param name="end">Curve end (screen space).</param>
    /// <param name="random">Injected RNG for tests and reproducible previews.</param>
    /// <param name="degree">When null, cubic or quintic is chosen at random.</param>
    public static IReadOnlyList<PointF> BuildPath(
        PointF start,
        PointF end,
        Random random,
        BezierPathDegree? degree = null)
    {
        ArgumentNullException.ThrowIfNull(random);

        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        int steps = ComputeStepCount(dist);

        BezierPathDegree deg = degree ?? (random.Next(2) == 0 ? BezierPathDegree.Cubic : BezierPathDegree.Quintic);
        PointF[] controls = deg == BezierPathDegree.Cubic
            ? BuildCubicControls(start, end, dx, dy, (float)dist, random)
            : BuildQuinticControls(start, end, dx, dy, (float)dist, random);

        var list = new List<PointF>(steps + 1);
        for (int i = 0; i <= steps; i++)
        {
            double s = i / (double)steps;
            double u = SmoothStep(s);
            list.Add(DeCasteljau(controls, u));
        }

        // Pin endpoints exactly to avoid drift from repeated lerp.
        if (list.Count > 0)
        {
            list[0] = start;
            list[^1] = end;
        }

        return list;
    }

    /// <summary>Evaluates an arbitrary-degree Bézier using De Casteljau's algorithm.</summary>
    public static PointF DeCasteljau(ReadOnlySpan<PointF> controlPoints, double t)
    {
        if (controlPoints.Length == 0)
            return default;
        t = Math.Clamp(t, 0, 1);
        Span<PointF> tmp = stackalloc PointF[controlPoints.Length];
        controlPoints.CopyTo(tmp);
        int n = tmp.Length;
        for (int r = 1; r < n; r++)
        {
            for (int i = 0; i < n - r; i++)
                tmp[i] = Lerp(tmp[i], tmp[i + 1], (float)t);
        }

        return tmp[0];
    }

    private static PointF[] BuildCubicControls(PointF p0, PointF p3, float vx, float vy, float len, Random rng)
    {
        var pts = new PointF[4];
        pts[0] = p0;
        pts[3] = p3;
        if (len < 1e-3f)
        {
            pts[1] = new PointF(p0.X + 4f, p0.Y);
            pts[2] = new PointF(p3.X - 4f, p3.Y);
            return pts;
        }

        float invLen = 1f / len;
        float tx = vx * invLen;
        float ty = vy * invLen;
        // Perpendicular (left-hand normal).
        float px = -ty;
        float py = tx;
        float band = 0.15f * len;

        float Along(Random r, double a, double b) => (float)(a + r.NextDouble() * (b - a));

        float t1 = Along(rng, 0.22, 0.48);
        float t2 = Along(rng, 0.52, 0.78);
        float o1 = (float)((rng.NextDouble() * 2 - 1) * band);
        float o2 = (float)((rng.NextDouble() * 2 - 1) * band);

        pts[1] = new PointF(
            p0.X + tx * len * t1 + px * o1,
            p0.Y + ty * len * t1 + py * o1);
        pts[2] = new PointF(
            p0.X + tx * len * t2 + px * o2,
            p0.Y + ty * len * t2 + py * o2);
        return pts;
    }

    private static PointF[] BuildQuinticControls(PointF p0, PointF p5, float vx, float vy, float len, Random rng)
    {
        var pts = new PointF[6];
        pts[0] = p0;
        pts[5] = p5;
        if (len < 1e-3f)
        {
            for (int i = 1; i < 5; i++)
                pts[i] = Lerp(p0, p5, i / 5f);
            return pts;
        }

        float invLen = 1f / len;
        float tx = vx * invLen;
        float ty = vy * invLen;
        float px = -ty;
        float py = tx;
        float band = 0.15f * len;

        float[] fracs = [0.15f, 0.35f, 0.65f, 0.85f];
        for (int i = 0; i < 4; i++)
        {
            float f = fracs[i];
            float off = (float)((rng.NextDouble() * 2 - 1) * band);
            pts[i + 1] = new PointF(
                p0.X + vx * f + px * off,
                p0.Y + vy * f + py * off);
        }

        return pts;
    }

    private static PointF Lerp(PointF a, PointF b, float t)
        => new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
}
