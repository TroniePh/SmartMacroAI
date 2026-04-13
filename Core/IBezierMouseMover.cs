using System.Drawing;

namespace SmartMacroAI.Core;

/// <summary>
/// Async, humanized physical mouse movement using Bézier paths, optional jitter,
/// and per-step absolute <c>SendInput</c> mouse moves (never batched multi-step injection).
/// </summary>
public interface IBezierMouseMover
{
    /// <summary>Moves the system cursor to <paramref name="target"/> (screen coordinates).</summary>
    Task MoveToAsync(Point target, MouseProfile profile = MouseProfile.Normal, CancellationToken ct = default);

    /// <summary>Moves to <paramref name="target"/> then performs a button down/up pair.</summary>
    Task MoveAndClickAsync(
        Point target,
        MouseButton button = MouseButton.Left,
        MouseProfile profile = MouseProfile.Normal,
        CancellationToken ct = default);

    /// <summary>Drags from <paramref name="from"/> to <paramref name="to"/> (screen coordinates).</summary>
    Task DragAsync(
        Point from,
        Point to,
        MouseProfile profile = MouseProfile.Normal,
        CancellationToken ct = default);

    /// <summary>When false, Gaussian jitter along the path is suppressed.</summary>
    void SetJitterEnabled(bool enabled);

    /// <summary>When false, overshoot-and-correct segments are never inserted.</summary>
    void SetOvershootEnabled(bool enabled);
}
