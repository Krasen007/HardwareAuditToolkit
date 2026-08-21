using HardwareAuditToolkit.Core.Keyboard;
using HardwareAuditToolkit.Core;

namespace HardwareAuditToolkit.Core.Messages;

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

    /// <summary>
    /// How many times this individual key has been pressed this run (the repeat
    /// counter). Drives the per-key repeat badge so a repeated press is visually
    /// distinct from a single press (§10 Phase 3 improvement).
    /// </summary>
    public int PressCount { get; init; }

    /// <summary>Human-readable press line for the operator's key-press log.</summary>
    public string LogLine { get; init; } = string.Empty;
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
