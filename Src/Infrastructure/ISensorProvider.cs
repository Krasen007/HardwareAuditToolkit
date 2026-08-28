namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// Best-effort hardware sensor provider contract (Phase 2 will supply a
/// LibreHardwareMonitorLib-backed implementation). May expose no sensors at
/// all when privileges are insufficient — consumers must treat an empty
/// reading set as "unavailable", never as an error.
/// </summary>
public interface ISensorProvider : IDisposable
{
    /// <summary>Begins background sensor polling.</summary>
    void Start();

    /// <summary>Stops background sensor polling.</summary>
    void Stop();

    /// <summary>Latest normalized readings.</summary>
    IReadOnlyList<SensorReading> ReadAll();

    /// <summary>
    /// When sensors are unavailable, a human-readable reason (e.g. "run as
    /// administrator for core temperatures"). Null when readings are present.
    /// </summary>
    string? UnavailableReason { get; }
}
