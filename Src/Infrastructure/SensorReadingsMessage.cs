namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// Carries a snapshot of the latest sensor readings across the in-process event
/// bus (architecture §3: SensorProvider "on the Event Bus"). Any screen that
/// wants live thermal/load/clock data subscribes to this message and re-binds
/// its view on arrival.
/// </summary>
public sealed class SensorReadingsMessage
{
    public IReadOnlyList<SensorReading> Readings { get; init; } = [];
    public string? UnavailableReason { get; init; }
}
