namespace HardwareAuditToolkit.Core.Messages;

/// <summary>
/// Monitor test lifecycle/status broadcast (start, confirm, flag, cancel).
/// Drives the view model's running/completed flags and the final status text
/// (architecture §10 Phase 5).
/// </summary>
public sealed class MonitorTestStatusMessage
{
    /// <summary>Current status — <see cref="HardwareAuditToolkit.Core.TestStatus.Running"/> while active, otherwise the terminal status.</summary>
    public Core.TestStatus Status { get; init; }

    /// <summary>Human-readable detail for the operator.</summary>
    public string? Detail { get; init; }
}
