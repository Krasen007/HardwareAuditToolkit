namespace HardwareAuditToolkit.Core.Reporting;

/// <summary>
/// Renders an <see cref="AuditSession"/> into a human-readable report (architecture §10
/// Phase 6 — "HTML report template"). Plain HTML keeps the dependency surface at zero
/// (architecture §2) and makes the report trivially printable to PDF.
/// </summary>
public interface IReportTemplate
{
    /// <summary>Produces the full report document for the given session.</summary>
    string Render(AuditSession session);
}
