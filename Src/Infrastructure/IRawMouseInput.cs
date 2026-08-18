namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// A single raw mouse sample. Mirrors <see cref="RawKeySample"/> but for the
/// pointer: button transitions, wheel data, and relative movement. Raw input
/// carries no absolute cursor position (only deltas), so tracing/position work
/// in the view uses WPF mouse events; this stream is the source of truth for
/// button/scroll/drag detection (architecture §10 Phase 4).
/// </summary>
public sealed class RawMouseSample
{
    /// <summary>Button transitions present in this sample (down/up/wheel flags).</summary>
    public MouseButtonChanges Buttons { get; init; }

    /// <summary>Signed wheel delta when <see cref="MouseButtonChanges.Wheel"/> is set.</summary>
    public int WheelDelta { get; init; }

    /// <summary>Relative horizontal movement since the last sample.</summary>
    public int DeltaX { get; init; }

    /// <summary>Relative vertical movement since the last sample.</summary>
    public int DeltaY { get; init; }

    /// <summary>True when the sample carries any movement (relative deltas non-zero).</summary>
    public bool HasMovement => DeltaX != 0 || DeltaY != 0;

    /// <summary>True when the sample carries a button transition.</summary>
    public bool IsButtonEvent => Buttons != MouseButtonChanges.None
                                  && (Buttons & MouseButtonChanges.Wheel) == 0;

    /// <summary>True when the sample carries a wheel event.</summary>
    public bool IsWheel => (Buttons & MouseButtonChanges.Wheel) != 0;
}

/// <summary>
/// Button transition flags produced by <see cref="IRawMouseInput"/>. A single
/// raw sample can carry several (e.g. left-up and right-down), hence the flags.
/// </summary>
[Flags]
public enum MouseButtonChanges
{
    /// <summary>No button/wheel activity.</summary>
    None = 0,

    /// <summary>Left button pressed.</summary>
    LeftDown = 1 << 0,

    /// <summary>Left button released.</summary>
    LeftUp = 1 << 1,

    /// <summary>Right button pressed.</summary>
    RightDown = 1 << 2,

    /// <summary>Right button released.</summary>
    RightUp = 1 << 3,

    /// <summary>Middle button pressed.</summary>
    MiddleDown = 1 << 4,

    /// <summary>Middle button released.</summary>
    MiddleUp = 1 << 5,

    /// <summary>Fourth (X1) button pressed.</summary>
    X1Down = 1 << 6,

    /// <summary>Fourth (X1) button released.</summary>
    X1Up = 1 << 7,

    /// <summary>Fifth (X2) button pressed.</summary>
    X2Down = 1 << 8,

    /// <summary>Fifth (X2) button released.</summary>
    X2Up = 1 << 9,

    /// <summary>Wheel scrolled; <see cref="RawMouseSample.WheelDelta"/> holds the signed delta.</summary>
    Wheel = 1 << 10,
}

/// <summary>
/// Captures raw mouse input independent of focus. Implementations must not block
/// the calling thread in their input callback — parse and raise
/// <see cref="MouseReceived"/> promptly so the WPF dispatcher stays responsive
/// (architecture §9.2 / Phase 4 gotchas).
/// </summary>
public interface IRawMouseInput
{
    /// <summary>Raised for each raw mouse event captured while active.</summary>
    event EventHandler<RawMouseSample>? MouseReceived;

    /// <summary>Begin capturing raw mouse input (idempotent).</summary>
    void Start();

    /// <summary>Stop capturing and release the underlying window/registration.</summary>
    void Stop();
}
