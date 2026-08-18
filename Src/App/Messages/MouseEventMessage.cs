using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;

namespace HardwareAuditToolkit.App.Messages;

/// <summary>
/// Live per-event update broadcast by <see cref="Modules.MouseTestModule"/> as
/// raw mouse samples arrive (architecture §3 event bus; §10 Phase 4). The mouse
/// view model subscribes and appends the line to the click/scroll/drag log and
/// updates the summary counters. The tracing sub-screen works from WPF pointer
/// events instead and ignores these (like the keyboard WPM sub-screen).
/// </summary>
public sealed class MouseEventMessage
{
    /// <summary>Button transitions / wheel present in the sample.</summary>
    public MouseButtonChanges Buttons { get; init; }

    /// <summary>Signed wheel delta when <see cref="MouseButtonChanges.Wheel"/> is set.</summary>
    public int WheelDelta { get; init; }

    /// <summary>Relative movement deltas.</summary>
    public int DeltaX { get; init; }

    /// <summary>Relative movement deltas.</summary>
    public int DeltaY { get; init; }

    /// <summary>True when the sample carried a button transition.</summary>
    public bool IsButtonEvent { get; init; }

    /// <summary>True when the sample carried a wheel event.</summary>
    public bool IsWheel { get; init; }

    /// <summary>True when the sample carried any movement.</summary>
    public bool HasMovement { get; init; }

    /// <summary>Human-readable log line for the operator.</summary>
    public string LogLine { get; init; } = string.Empty;

    /// <summary>Running left-click count (cumulative this run).</summary>
    public int LeftClicks { get; init; }

    /// <summary>Running right-click count (cumulative this run).</summary>
    public int RightClicks { get; init; }

    /// <summary>Running middle-click count (cumulative this run).</summary>
    public int MiddleClicks { get; init; }

    /// <summary>Running wheel-tick count (cumulative this run).</summary>
    public int WheelTicks { get; init; }

    /// <summary>Running drag count (cumulative this run).</summary>
    public int Drags { get; init; }
}

/// <summary>
/// Mouse test lifecycle/status broadcast (start, confirm, flag, cancel).
/// Drives the view model's running/completed flags and the final status text.
/// </summary>
public sealed class MouseTestStatusMessage
{
    /// <summary>Current status — <see cref="TestStatus.Running"/> while active, otherwise the
    /// terminal <see cref="TestStatus"/>.</summary>
    public TestStatus Status { get; init; }

    /// <summary>Human-readable detail for the operator.</summary>
    public string? Detail { get; init; }
}
