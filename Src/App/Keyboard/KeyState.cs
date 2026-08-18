namespace HardwareAuditToolkit.App.Keyboard;

/// <summary>
/// Per-key lifecycle state in the keyboard test (architecture §10 Phase 3):
/// <c>Untested → Pressed → Confirmed</c>. <see cref="Pressed"/> means the key
/// has registered at least once; <see cref="Confirmed"/> means the operator
/// accepted the keyboard as working (the recorded result status).
/// </summary>
public enum KeyState
{
    /// <summary>The key has not yet registered this run.</summary>
    Untested,

    /// <summary>The key has registered at least once.</summary>
    Pressed,

    /// <summary>The operator confirmed the keyboard works; pressed keys are
    /// promoted to confirmed for the report.</summary>
    Confirmed,
}
