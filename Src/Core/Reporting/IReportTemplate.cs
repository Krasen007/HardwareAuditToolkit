namespace HardwareAuditToolkit.Core.Reporting;

/// <summary>
/// Renders a <see cref="ReportModel"/> into a human-readable report (architecture §10
/// Phase 6 — "HTML report template"). Plain HTML keeps the dependency surface at zero
/// (architecture §2) and makes the report trivially printable to PDF.
/// </summary>
public interface IReportTemplate
{
    /// <summary>Produces the full report document for the given report model.</summary>
    string Render(ReportModel model);
}
