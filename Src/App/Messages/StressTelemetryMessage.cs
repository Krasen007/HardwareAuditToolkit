using HardwareAuditToolkit.Core;

namespace HardwareAuditToolkit.App.Messages;

/// <summary>
/// Live telemetry broadcast by <see cref="Modules.CpuStressModule"/> while a burn-in
/// run is active (and one final sample on completion). The stress view model
/// subscribes and rebinds its live readouts on each arrival (architecture §3
/// event bus; §8 fixed-duration burn-in).
/// </summary>
public sealed class StressTelemetryMessage
{
    /// <summary>Number of logical cores the burn-in is loading.</summary>
    public int CoreCount { get; init; }

    /// <summary>Elapsed run time so far.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Configured target duration (the §8 fixed cap or operator choice).</summary>
    public TimeSpan TargetDuration { get; init; }

    /// <summary>System-wide CPU load percent if a sensor is available, else null.</summary>
    public double? CpuLoadPercent { get; init; }

    /// <summary>Per-core (or aggregate) temperatures in °C if available.</summary>
    public IReadOnlyList<float?> CoreTempsCelsius { get; init; } = Array.Empty<float?>();

    /// <summary>True while the run is in progress.</summary>
    public bool Running { get; init; }

    /// <summary>Final status when the run has ended; null while running.</summary>
    public TestStatus? FinalStatus { get; init; }
}
