namespace HardwareAuditToolkit.Core.Messages;

/// <summary>
/// Published by the App device-change service whenever a keyboard/mouse
/// arrives/leaves or the display topology changes (§9.5).
/// </summary>
public sealed class DeviceTopologyChangedMessage
{
    public int KeyboardCount { get; init; }
    public int MouseCount { get; init; }
    public int MonitorCount { get; init; }
    public string Detail { get; init; } = string.Empty;
}
