namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// A single normalized sensor reading from the LibreHardwareMonitor adapter.
/// All values are best-effort and may be unavailable without elevation.
/// </summary>
public readonly record struct SensorReading(
    string HardwareName,
    string SensorName,
    string SensorType,
    float? Value,
    string Unit);
