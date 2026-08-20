namespace HardwareAuditToolkit.Core.Messages;

/// <summary>
/// Marker message raised by every exit path (Ctrl+E hook, the Exit Test overlay,
/// the header button) and routed through the orchestrator's exit flow (§6).
/// The recipient marshals the actual shutdown onto the UI thread so the
/// low-level hook thread is never blocked.
/// </summary>
public sealed class ExitRequestedMessage
{
}
