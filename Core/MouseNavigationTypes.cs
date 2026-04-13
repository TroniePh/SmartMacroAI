namespace SmartMacroAI.Core;

/// <summary>Humanization preset for physical cursor movement (pixels per millisecond bands).</summary>
public enum MouseProfile
{
    /// <summary>~0.5–0.9 px/ms.</summary>
    Relaxed,
    /// <summary>~0.8–1.4 px/ms (default).</summary>
    Normal,
    /// <summary>~1.5–2.5 px/ms.</summary>
    Fast,
    /// <summary>Teleport with <c>SetCursorPos</c>; Bézier path is skipped.</summary>
    Instant,
}

/// <summary>Mouse button used with low-level click injection.</summary>
public enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// <summary>Polynomial degree used for the humanized path (randomized per move).</summary>
public enum BezierPathDegree
{
    Cubic,
    Quintic,
}
