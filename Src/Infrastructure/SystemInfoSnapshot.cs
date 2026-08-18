namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// A point-in-time inventory of the machine being audited (Phase 2).
/// Populated best-effort from WMI/CIM; any single query failing simply leaves
/// the corresponding field null rather than throwing (architecture §7, Phase 7
/// degradation rule).
/// </summary>
public sealed class SystemInfoSnapshot
{
    public string? CpuName { get; set; }
    public int? PhysicalCores { get; set; }
    public int? LogicalProcessors { get; set; }
    public long? MaxClockSpeedMhz { get; set; }

    public long? TotalRamBytes { get; set; }
    public string? TotalRamFormatted
        => TotalRamBytes.HasValue
            ? $"{TotalRamBytes.Value / 1024.0 / 1024 / 1024:0.0} GB"
            : null;

    public string? OperatingSystem { get; set; }
    public string? OsArchitecture { get; set; }

    public string? BiosVersion { get; set; }
    public string? BiosManufacturer { get; set; }
    public string? Motherboard { get; set; }
    public string? SystemManufacturer { get; set; }
    public string? SystemModel { get; set; }

    /// <summary>"Model — Size" strings for each detected fixed disk.</summary>
    public List<string> Disks { get; set; } = new();

    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}
