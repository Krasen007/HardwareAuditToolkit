namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// Best-effort diagnostic sink (architecture §9.7 "a fault is never silent").
/// Consumers may pass <c>null</c> to drop diagnostics entirely; an
/// implementation must never throw into the caller's fault-handling path.
/// </summary>
public interface IDiagnosticLog
{
    /// <summary>Records a message. Must not throw.</summary>
    void Write(string message);
}