using System.Drawing;
using SmartMacroAI.Core;
using Xunit;

namespace SmartMacroAI.Tests;

public sealed class BezierCurveGeneratorTests
{
    [Fact]
    public void ComputeStepCount_IsWithinBounds()
    {
        Assert.Equal(BezierCurveGenerator.MinSteps, BezierCurveGenerator.ComputeStepCount(0));
        Assert.InRange(BezierCurveGenerator.ComputeStepCount(50), BezierCurveGenerator.MinSteps, BezierCurveGenerator.MaxSteps);
        Assert.Equal(BezierCurveGenerator.MaxSteps, BezierCurveGenerator.ComputeStepCount(1_000_000));
    }

    [Theory]
    [InlineData(BezierPathDegree.Cubic)]
    [InlineData(BezierPathDegree.Quintic)]
    public void BuildPath_StartAndEndMatch(BezierPathDegree degree)
    {
        var start = new PointF(10f, 20f);
        var end = new PointF(400f, 250f);
        var rng = new Random(12345);
        IReadOnlyList<PointF> path = BezierCurveGenerator.BuildPath(start, end, rng, degree);

        Assert.True(path.Count >= BezierCurveGenerator.MinSteps + 1);
        Assert.Equal(BezierCurveGenerator.ComputeStepCount(
            Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2))) + 1,
            path.Count);

        Assert.Equal(start.X, path[0].X, 2f);
        Assert.Equal(start.Y, path[0].Y, 2f);
        Assert.Equal(end.X, path[^1].X, 2f);
        Assert.Equal(end.Y, path[^1].Y, 2f);
    }

    [Fact]
    public void SmoothStep_EndpointsAreZeroAndOne()
    {
        Assert.Equal(0, BezierCurveGenerator.SmoothStep(0), 9);
        Assert.Equal(1, BezierCurveGenerator.SmoothStep(1), 9);
        double mid = BezierCurveGenerator.SmoothStep(0.5);
        Assert.InRange(mid, 0.45, 0.55);
    }
}
