using HardwareAuditToolkit.App.Keyboard;
using HardwareAuditToolkit.Core;

namespace HardwareAuditToolkit.App.Messages;

/// <summary>
/// Live per-key update broadcast by <see cref="Modules.KeyboardTestModule"/> as
/// raw key samples arrive (architecture §3 event bus; §10 Phase 3). The keyboard
/// view model subscribes and rebinds the matching tile's state; the WPM sub-screen
/// ignores these (it works from typed text instead).
/// </summary>
public sealed class KeyEventMessage
{
    /// <summary>Composite scan-code id of the key (matches <see cref="KeyboardLayout"/>).</summary>
    public int KeyId { get; init; }

    /// <summary>On-screen label of the key.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>True for a key-down sample.</summary>
    public bool IsKeyDown { get; init; }

    /// <summary>The key's new lifecycle state after this sample.</summary>
    public KeyState NewState { get; init; }

    /// <summary>Keys registered so far (coverage numerator).</summary>
    public int PressedCount { get; init; }

    /// <summary>Total expected keys (coverage denominator).</summary>
    public int ExpectedCount { get; init; }
}

/// <summary>
/// Keyboard test lifecycle/status broadcast (start, confirm, flag, cancel).
/// Drives the view model's running/completed flags and the final status text.
/// </summary>
public sealed class KeyboardTestStatusMessage
{
    /// <summary>Current status — <see cref="Running"/> while active, otherwise the
    /// terminal <see cref="TestStatus"/>.</summary>
    public TestStatus Status { get; init; }

    /// <summary>Human-readable detail for the operator.</summary>
    public string? Detail { get; init; }
}
