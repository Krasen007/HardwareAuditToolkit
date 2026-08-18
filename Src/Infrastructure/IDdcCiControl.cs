namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// Describes a physical display as discovered by <see cref="IDdcCiControl"/>.
/// Coordinates are in raw device pixels (the same space used by
/// <c>EnumDisplayMonitors</c>), so a fullscreen pattern window can be placed on
/// the correct monitor regardless of DPI without extra conversion math (§9.4).
/// </summary>
public sealed record MonitorInfo
{
    /// <summary>Stable index within the current enumeration (topology-dependent).</summary>
    public int Index { get; init; }

    /// <summary>Device path, e.g. <c>\\.\DISPLAY1</c>.</summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>Human-readable monitor name (from DDC/CI when available, else the device path).</summary>
    public string FriendlyName { get; init; } = string.Empty;

    /// <summary>True for the primary display.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>Left edge in device pixels of the virtual screen.</summary>
    public int Left { get; init; }

    /// <summary>Top edge in device pixels of the virtual screen.</summary>
    public int Top { get; init; }

    /// <summary>Width in device pixels.</summary>
    public int Width { get; init; }

    /// <summary>Height in device pixels.</summary>
    public int Height { get; init; }
}

/// <summary>
/// Result of a brightness (VCP 0x10) probe. <see cref="Supported"/> is false when
/// DDC/CI is unavailable or disabled, in which case <see cref="Detail"/> explains
/// why — the rest of the value is meaningless (architecture §10 Phase 5, graceful
/// "unsupported" handling).
/// </summary>
public sealed record BrightnessReading
{
    public bool Supported { get; init; }
    public int Current { get; init; }
    public int Minimum { get; init; }
    public int Maximum { get; init; }
    public string? Detail { get; init; }
}

/// <summary>
/// <para>
/// Phase 5 — DDC/CI control surface (architecture §10 Phase 5, §2). Wraps
/// <c>dxva2.dll</c> to enumerate physical monitors and read/set the VCP 0x10
/// (brightness) feature. Everything degrades gracefully: a missing driver, a
/// monitor without DDC/CI, or a best-effort-without-admin failure all return
/// <see cref="BrightnessReading.Supported"/> == false rather than throwing.
/// </para>
/// </summary>
public interface IDdcCiControl
{
    /// <summary>Current display topology; reflects live hot-plug/reconfiguration (§9.5).</summary>
    IReadOnlyList<MonitorInfo> EnumerateMonitors();

    /// <summary>Probes brightness support for the given display index.</summary>
    BrightnessReading GetBrightness(int index);

    /// <summary>Sets brightness (0–100 scale mapped to the monitor's reported range). Returns false when unsupported.</summary>
    bool SetBrightness(int index, int value);
}
