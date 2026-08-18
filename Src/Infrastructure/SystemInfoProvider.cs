using System.Management;

namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// <para>
/// Phase 2 — WMI/CIM inventory provider. Collects CPU, RAM, disk, BIOS and
/// motherboard data without elevation (architecture §2 "System inventory").
/// </para>
/// <para>
/// Every query is wrapped so a single failure degrades to a null field instead
/// of aborting the whole inventory (architecture §7 / Phase 7 degradation rule).
/// The snapshot is computed once and cached; subsequent calls return the same
/// data so the live view and the recorded audit findings agree.
/// </para>
/// </summary>
public sealed class SystemInfoProvider : IDisposable
{
    private SystemInfoSnapshot? _cached;
    private readonly object _gate = new();

    /// <summary>Returns a cached inventory snapshot, computing it on first call.</summary>
    public SystemInfoSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var snapshot = new SystemInfoSnapshot();
            CollectCpu(snapshot);
            CollectMemory(snapshot);
            CollectOperatingSystem(snapshot);
            CollectBiosAndBoard(snapshot);
            CollectDisks(snapshot);
            snapshot.CapturedAt = DateTime.UtcNow;
            _cached = snapshot;
            return snapshot;
        }
    }

    private static void CollectCpu(SystemInfoSnapshot s)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                s.CpuName ??= obj["Name"]?.ToString()?.Trim();
                if (s.PhysicalCores is null && obj["NumberOfCores"] is not null)
                {
                    int.TryParse(obj["NumberOfCores"].ToString(), out int cores);
                    s.PhysicalCores = cores;
                }

                if (s.LogicalProcessors is null && obj["NumberOfLogicalProcessors"] is not null)
                {
                    int.TryParse(obj["NumberOfLogicalProcessors"].ToString(), out int logical);
                    s.LogicalProcessors = logical;
                }

                if (s.MaxClockSpeedMhz is null && obj["MaxClockSpeed"] is not null)
                {
                    long.TryParse(obj["MaxClockSpeed"].ToString(), out long mhz);
                    s.MaxClockSpeedMhz = mhz;
                }
            }
        }
        catch (ManagementException)
        {
            // Best-effort: leave CPU fields null.
        }
    }

    private static void CollectMemory(SystemInfoSnapshot s)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory, Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["TotalPhysicalMemory"] is not null &&
                    ulong.TryParse(obj["TotalPhysicalMemory"].ToString(), out ulong bytes))
                {
                    s.TotalRamBytes = (long)bytes;
                }

                s.SystemManufacturer ??= obj["Manufacturer"]?.ToString()?.Trim();
                s.SystemModel ??= obj["Model"]?.ToString()?.Trim();
            }
        }
        catch (ManagementException)
        {
            // Best-effort.
        }
    }

    private static void CollectOperatingSystem(SystemInfoSnapshot s)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, OSArchitecture FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                s.OperatingSystem ??= obj["Caption"]?.ToString()?.Trim();
                s.OsArchitecture ??= obj["OSArchitecture"]?.ToString()?.Trim();
            }
        }
        catch (ManagementException)
        {
            // Best-effort.
        }
    }

    private static void CollectBiosAndBoard(SystemInfoSnapshot s)
    {
        try
        {
            using var bios = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, Manufacturer FROM Win32_BIOS");
            foreach (ManagementObject obj in bios.Get())
            {
                s.BiosVersion ??= obj["SMBIOSBIOSVersion"]?.ToString()?.Trim();
                s.BiosManufacturer ??= obj["Manufacturer"]?.ToString()?.Trim();
            }
        }
        catch (ManagementException)
        {
            // Best-effort.
        }

        try
        {
            using var board = new ManagementObjectSearcher("SELECT Product, Manufacturer FROM Win32_BaseBoard");
            foreach (ManagementObject obj in board.Get())
            {
                var product = obj["Product"]?.ToString()?.Trim();
                var manufacturer = obj["Manufacturer"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(manufacturer) || !string.IsNullOrEmpty(product))
                {
                    s.Motherboard = string.Join(" ", new[] { manufacturer, product }.Where(x => !string.IsNullOrEmpty(x))).Trim();
                }
            }
        }
        catch (ManagementException)
        {
            // Best-effort.
        }
    }

    private static void CollectDisks(SystemInfoSnapshot s)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model, Size, MediaType FROM Win32_DiskDrive WHERE MediaType LIKE '%Fixed%'");
            foreach (ManagementObject obj in searcher.Get())
            {
                var model = obj["Model"]?.ToString()?.Trim() ?? "Unknown disk";
                string? size = null;
                if (obj["Size"] is not null && ulong.TryParse(obj["Size"].ToString(), out ulong bytes))
                {
                    size = $"{bytes / 1024.0 / 1024 / 1024 / 1024:0.0} GB";
                }

                s.Disks.Add(string.IsNullOrEmpty(size) ? model : $"{model} — {size}");
            }
        }
        catch (ManagementException)
        {
            // Best-effort.
        }
    }

    public void Dispose()
    {
        // No unmanaged resources; provided for DI symmetry (IDisposable).
    }
}
