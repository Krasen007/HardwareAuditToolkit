namespace HardwareAuditToolkit.Core;

/// <summary>
/// Lifecycle phase of a test module execution (§5):
/// <c>Setup → Running → AwaitingOperatorConfirmation → Complete</c>.
/// </summary>
public enum ModulePhase
{
    /// <summary>The module has not been started this session.</summary>
    NotStarted,

    /// <summary>The module is acquiring resources / preparing its UI.</summary>
    Setup,

    /// <summary>The module is actively measuring.</summary>
    Running,

    /// <summary>The module is waiting for the technician to confirm a
    /// perceptual check (e.g. monitor uniformity, tracing accuracy).</summary>
    AwaitingOperatorConfirmation,

    /// <summary>The module finished and reported its result.</summary>
    Complete,

    /// <summary>The module was cancelled before finishing.</summary>
    Cancelled,
}
