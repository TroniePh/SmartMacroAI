using System.Diagnostics;
using System.Drawing;
using SmartMacroAI.Localization;

namespace SmartMacroAI.Core;

/// <summary>
/// Thread-safe humanized cursor movement with Bézier paths, Gaussian jitter,
/// high-resolution step pacing (<see cref="Stopwatch"/> + <see cref="Thread.SpinWait"/>),
/// and per-step <see cref="Win32MouseInput.SendMouseMoveAbsolute"/>.
/// </summary>
public sealed class BezierMouseMover : IBezierMouseMover
{
    private readonly object _gate = new();

    private bool _jitterEnabled = true;
    private bool _overshootEnabled = true;
    private bool _microPauseEnabled = true;
    private int _jitterIntensityPercent = 50;
    private bool _rawInputBypass;
    private bool _hardwareSimulationRequested;
    private IntPtr _rawNotifyHwnd = IntPtr.Zero;
    private int _loggedInterceptionFallback;

    /// <summary>Raised for each completed move (from, to, distance, duration, profile, point count).</summary>
    public Action<string>? DiagnosticLog { get; set; }

    /// <summary>Reloads toggles from persisted <see cref="AppSettings"/> (call before each macro run).</summary>
    public void ReloadFromAppSettings()
    {
        var app = AppSettings.Load();
        lock (_gate)
        {
            _jitterIntensityPercent = Math.Clamp(app.MouseJitterIntensity, 0, 100);
            _overshootEnabled = app.MouseOvershootEnabled;
            _microPauseEnabled = app.MouseMicroPauseEnabled;
            _rawInputBypass = app.MouseRawInputBypass;
            _hardwareSimulationRequested = app.MouseHardwareSimulationDriver;
        }
    }

    /// <summary>Sets the HWND used for optional <see cref="Win32MouseInput.SetCursorAndNotifyWindowMove"/>.</summary>
    public void SetRawInputNotifyWindow(IntPtr hwnd)
    {
        lock (_gate) _rawNotifyHwnd = hwnd;
    }

    /// <inheritdoc />
    public void SetJitterEnabled(bool enabled)
    {
        lock (_gate) _jitterEnabled = enabled;
    }

    /// <inheritdoc />
    public void SetOvershootEnabled(bool enabled)
    {
        lock (_gate) _overshootEnabled = enabled;
    }

    /// <inheritdoc />
    public async Task MoveToAsync(Point target, MouseProfile profile = MouseProfile.Normal, CancellationToken ct = default)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        bool rawBypassCopy;
        bool hwCopy;
        IntPtr hwndCopy;
        int jitterPct;
        bool jitterOn;
        bool overshootOn;
        bool microPauseOn;
        lock (_gate)
        {
            rawBypassCopy = _rawInputBypass;
            hwCopy = _hardwareSimulationRequested;
            hwndCopy = _rawNotifyHwnd;
            jitterPct = _jitterIntensityPercent;
            jitterOn = _jitterEnabled;
            overshootOn = _overshootEnabled;
            microPauseOn = _microPauseEnabled;
        }

        MaybeLogInterceptionFallback(hwCopy);
        Point from = Win32MouseInput.GetCursorScreenPoint();
        if (profile == MouseProfile.Instant)
        {
            var swInst = Stopwatch.StartNew();
            if (rawBypassCopy && hwndCopy != IntPtr.Zero)
                Win32MouseInput.SetCursorAndNotifyWindowMove(hwndCopy, target.X, target.Y);
            else
                Win32MouseInput.SetCursorPos(target.X, target.Y);
            swInst.Stop();
            LogMove(from, target, profile, swInst, pathPointCount: 1);
            return;
        }

        var sw = Stopwatch.StartNew();
        var rng = Random.Shared;
        double speedPxMs = SampleSpeedPxMs(profile, rng);
        IReadOnlyList<PointF> path = BuildJitteredPath(from, target, rng, jitterOn, jitterPct, speedPxMs);
        int pauseIndex = microPauseOn && path.Count > 12 && rng.Next(2) == 0
            ? rng.Next(path.Count / 4, (path.Count * 3) / 4)
            : -1;
        int pauseMs = microPauseOn && pauseIndex >= 0 ? rng.Next(8, 26) : 0;

        await MoveAlongPathAsync(path, speedPxMs, pauseIndex, pauseMs, rawBypassCopy, hwndCopy, ct).ConfigureAwait(false);

        if (overshootOn && rng.NextDouble() < 0.30)
            await ApplyOvershootAndCorrectAsync(target, rng, rawBypassCopy, hwndCopy, ct).ConfigureAwait(false);

        sw.Stop();
        LogMove(from, target, profile, sw, path.Count);
    }

    /// <inheritdoc />
    public async Task MoveAndClickAsync(
        Point target,
        MouseButton button = MouseButton.Left,
        MouseProfile profile = MouseProfile.Normal,
        CancellationToken ct = default)
    {
        await MoveToAsync(target, profile, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        int j = Random.Shared.Next(-2, 3);
        Win32MouseInput.SendMouseButtonDown(button, j);
        SpinWaitMs(Random.Shared.Next(12, 35));
        Win32MouseInput.SendMouseButtonUp(button, Random.Shared.Next(-2, 3));
    }

    /// <inheritdoc />
    public async Task DragAsync(
        Point from,
        Point to,
        MouseProfile profile = MouseProfile.Normal,
        CancellationToken ct = default)
    {
        await MoveToAsync(from, profile, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        Win32MouseInput.SendMouseButtonDown(MouseButton.Left, Random.Shared.Next(-2, 3));
        await MoveToAsync(to, profile, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        Win32MouseInput.SendMouseButtonUp(MouseButton.Left, Random.Shared.Next(-2, 3));
    }

    private void MaybeLogInterceptionFallback(bool hwRequested)
    {
        if (!hwRequested || !InterceptionHardwareMouseProbe.IsDriverDllPresent())
            return;
        if (Interlocked.Exchange(ref _loggedInterceptionFallback, 1) != 0)
            return;
        DiagnosticLog?.Invoke(
            "[Mouse] interception.dll found — hardware driver API is not integrated; using per-step SendInput.");
    }

    private IReadOnlyList<PointF> BuildJitteredPath(
        Point from,
        Point target,
        Random rng,
        bool jitterOn,
        int jitterPct,
        double speedPxMs)
    {
        var path = BezierCurveGenerator.BuildPath(
            new PointF(from.X, from.Y),
            new PointF(target.X, target.Y),
            rng);

        if (!jitterOn || jitterPct <= 0)
            return path;

        // Sigma 0.3–0.8 px scaled by intensity; slightly higher when moving faster.
        double speedNorm = Math.Clamp((speedPxMs - 0.5) / 2.0, 0, 1);
        double sigmaBase = 0.3 + 0.5 * speedNorm;
        double sigma = sigmaBase * (jitterPct / 100.0);
        var copy = new PointF[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            float jx = (float)(NextGaussian(rng) * sigma);
            float jy = (float)(NextGaussian(rng) * sigma);
            // Keep endpoints clean.
            if (i == 0 || i == path.Count - 1)
                copy[i] = path[i];
            else
                copy[i] = new PointF(path[i].X + jx, path[i].Y + jy);
        }

        return copy;
    }

    private static double NextGaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    private async Task MoveAlongPathAsync(
        IReadOnlyList<PointF> path,
        double speedPxMs,
        int pauseIndex,
        int pauseMs,
        bool rawBypass,
        IntPtr hwnd,
        CancellationToken ct)
    {
        if (path.Count == 0)
            return;

        double chord = Distance(path[0], path[^1]);
        double arcHint = chord * 1.12;
        double durationMs = Math.Max(8, arcHint / Math.Max(0.05, speedPxMs));
        double stepMs = durationMs / Math.Max(1, path.Count - 1);
        var clock = Stopwatch.StartNew();
        double nextDeadlineMs = 0;

        for (int i = 0; i < path.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var p = path[i];
            int xi = (int)Math.Round(p.X);
            int yi = (int)Math.Round(p.Y);
            int tJitter = Random.Shared.Next(-2, 3);
            if (rawBypass && hwnd != IntPtr.Zero)
                Win32MouseInput.SetCursorAndNotifyWindowMove(hwnd, xi, yi);
            else
                Win32MouseInput.SendMouseMoveAbsolute(xi, yi, tJitter);

            if (i == pauseIndex && pauseMs > 0)
                SpinWaitMs(pauseMs);

            nextDeadlineMs += stepMs * (0.92 + Random.Shared.NextDouble() * 0.16);
            await PreciseWaitUntilAsync(clock, nextDeadlineMs, ct).ConfigureAwait(false);
        }
    }

    private async Task ApplyOvershootAndCorrectAsync(Point target, Random rng, bool rawBypass, IntPtr hwnd, CancellationToken ct)
    {
        double len = 2 + rng.NextDouble() * 2;
        double ang = rng.NextDouble() * Math.PI * 2;
        int ox = (int)Math.Round(target.X + Math.Cos(ang) * len);
        int oy = (int)Math.Round(target.Y + Math.Sin(ang) * len);
        var overshootPath = new PointF[]
        {
            new PointF(target.X, target.Y),
            new PointF(ox, oy),
            new PointF(target.X, target.Y),
        };
        await MoveAlongPathAsync(overshootPath, SampleSpeedPxMs(MouseProfile.Fast, rng), -1, 0, rawBypass, hwnd, ct)
            .ConfigureAwait(false);
    }

    private void LogMove(Point from, Point to, MouseProfile profile, Stopwatch sw, int pathPointCount)
    {
        double dist = Distance(new PointF(from.X, from.Y), new PointF(to.X, to.Y));
        DiagnosticLog?.Invoke(
            $"[MouseMove] from=({from.X},{from.Y}) to=({to.X},{to.Y}) dist={dist:F1}px " +
            $"duration_ms={sw.ElapsedMilliseconds} profile={profile} path_points={pathPointCount}");
    }

    private static double SampleSpeedPxMs(MouseProfile profile, Random rng) => profile switch
    {
        MouseProfile.Relaxed => 0.5 + rng.NextDouble() * 0.4,
        MouseProfile.Normal => 0.8 + rng.NextDouble() * 0.6,
        MouseProfile.Fast => 1.5 + rng.NextDouble() * 1.0,
        MouseProfile.Instant => 1000,
        _ => 1.0,
    };

    private static double Distance(PointF a, PointF b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static void SpinWaitMs(double ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < ms)
            Thread.SpinWait(64);
    }

    private static async Task PreciseWaitUntilAsync(Stopwatch clock, double deadlineMs, CancellationToken ct)
    {
        await Task.Yield();
        while (clock.Elapsed.TotalMilliseconds < deadlineMs)
        {
            ct.ThrowIfCancellationRequested();
            Thread.SpinWait(48);
        }
    }

    public static MouseProfile ParseProfile(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "relaxed" => MouseProfile.Relaxed,
        "fast" => MouseProfile.Fast,
        "instant" => MouseProfile.Instant,
        _ => MouseProfile.Normal,
    };
}
