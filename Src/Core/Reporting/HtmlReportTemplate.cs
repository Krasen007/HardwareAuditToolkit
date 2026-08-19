using System.Net;
using System.Text;

namespace HardwareAuditToolkit.Core.Reporting;

/// <summary>
/// Default HTML report renderer (architecture §10 Phase 6). Self-contained, no external
/// dependencies: inline CSS, a summary table, and a per-module detail section covering
/// status, timestamps, findings, measurements, operator actions, and artifacts. Designed
/// to print cleanly to PDF from any browser.
/// </summary>
public sealed class HtmlReportTemplate : IReportTemplate
{
    public string Render(AuditSession session)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine("  <title>Hardware Audit Report</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { font-family: Segoe UI, Arial, sans-serif; color: #222; margin: 24px; }");
        sb.AppendLine("    h1 { font-size: 22px; margin: 0 0 4px; }");
        sb.AppendLine("    h2 { font-size: 16px; margin: 24px 0 8px; border-bottom: 1px solid #ccc; padding-bottom: 4px; }");
        sb.AppendLine("    .meta { color: #555; font-size: 13px; }");
        sb.AppendLine("    .meta span { margin-right: 18px; }");
        sb.AppendLine("    table { border-collapse: collapse; width: 100%; font-size: 13px; }");
        sb.AppendLine("    th, td { border: 1px solid #ccc; padding: 6px 8px; text-align: left; vertical-align: top; }");
        sb.AppendLine("    th { background: #2D2D30; color: #fff; }");
        sb.AppendLine("    tr:nth-child(even) td { background: #f6f6f6; }");
        sb.AppendLine("    .status { font-weight: 600; }");
        sb.AppendLine("    .pass { color: #1e7e34; } .fail { color: #c0392b; } .warn { color: #b9770e; }");
        sb.AppendLine("    .na { color: #888; } .cancel { color: #6c3483; }");
        sb.AppendLine("    ul { margin: 4px 0 4px 18px; padding: 0; }");
        sb.AppendLine("    .notes { white-space: pre-wrap; }");
        sb.AppendLine("    @media print { body { margin: 12px; } }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        sb.AppendLine("  <h1>Hardware Audit Report</h1>");
        sb.AppendLine("  <div class=\"meta\">");
        sb.AppendLine($"    <span><b>Host:</b> {Enc(session.Hostname)}</span>");
        sb.AppendLine($"    <span><b>Session:</b> {Enc(session.SessionId)}</span>");
        sb.AppendLine($"    <span><b>Started:</b> {Enc(Fmt(session.StartedAt))}</span>");
        sb.AppendLine($"    <span><b>Completed:</b> {Enc(session.CompletedAt is { } c ? Fmt(c) : "in progress")}</span>");
        if (!string.IsNullOrEmpty(session.MachineId))
            sb.AppendLine($"    <span><b>Machine ID:</b> {Enc(session.MachineId)}</span>");
        sb.AppendLine("  </div>");
        sb.AppendLine($"  <p class=\"meta\">Overall status: <span class=\"status {StatusClass(session.OverallStatus)}\">{Enc(session.OverallStatus.ToString())}</span></p>");

        // Summary table.
        sb.AppendLine("  <h2>Modules</h2>");
        sb.AppendLine("  <table>");
        sb.AppendLine("    <thead><tr><th>Module</th><th>Status</th><th>Started</th><th>Completed</th></tr></thead>");
        sb.AppendLine("    <tbody>");
        foreach (var m in session.Modules)
        {
            sb.AppendLine("      <tr>");
            sb.AppendLine($"        <td>{Enc(m.DisplayName)}</td>");
            sb.AppendLine($"        <td class=\"status {StatusClass(m.Status)}\">{Enc(m.Status.ToString())}</td>");
            sb.AppendLine($"        <td>{Enc(m.StartedAt is { } ms ? Fmt(ms) : "-")}</td>");
            sb.AppendLine($"        <td>{Enc(m.CompletedAt is { } mc ? Fmt(mc) : "-")}</td>");
            sb.AppendLine("      </tr>");
        }
        if (session.Modules.Count == 0)
            sb.AppendLine("      <tr><td colspan=\"4\" class=\"na\">No modules were run in this session.</td></tr>");
        sb.AppendLine("    </tbody>");
        sb.AppendLine("  </table>");

        // Per-module detail.
        foreach (var m in session.Modules)
        {
            sb.AppendLine($"  <h2>{Enc(m.DisplayName)} — {Enc(m.Status.ToString())}</h2>");

            if (m.Findings.Count > 0)
            {
                sb.AppendLine("  <p><b>Findings</b></p><ul>");
                foreach (var f in m.Findings)
                    sb.AppendLine($"    <li>{Enc(f)}</li>");
                sb.AppendLine("  </ul>");
            }

            if (m.OperatorActions.Count > 0)
            {
                sb.AppendLine("  <p><b>Operator actions</b></p><ul>");
                foreach (var a in m.OperatorActions)
                    sb.AppendLine($"    <li>{Enc(a)}</li>");
                sb.AppendLine("  </ul>");
            }

            if (m.Measurements.Count > 0)
            {
                sb.AppendLine("  <p><b>Measurements</b></p>");
                sb.AppendLine("  <table>");
                sb.AppendLine("    <thead><tr><th>Time</th><th>Label</th><th>Value</th><th>Context</th></tr></thead>");
                sb.AppendLine("    <tbody>");
                foreach (var mm in m.Measurements)
                {
                    sb.AppendLine("      <tr>");
                    sb.AppendLine($"        <td>{Enc(Fmt(mm.Timestamp))}</td>");
                    sb.AppendLine($"        <td>{Enc(mm.Label)}</td>");
                    sb.AppendLine($"        <td>{Enc(mm.Value ?? "-")}</td>");
                    sb.AppendLine($"        <td>{Enc(mm.Context ?? "-")}</td>");
                    sb.AppendLine("      </tr>");
                }
                sb.AppendLine("    </tbody>");
                sb.AppendLine("  </table>");
            }

            if (m.Artifacts.Count > 0)
            {
                sb.AppendLine("  <p><b>Artifacts</b></p><ul>");
                foreach (var a in m.Artifacts)
                    sb.AppendLine($"    <li>{Enc(a)}</li>");
                sb.AppendLine("  </ul>");
            }
        }

        if (!string.IsNullOrWhiteSpace(session.Notes))
        {
            sb.AppendLine("  <h2>Notes</h2>");
            sb.AppendLine($"  <p class=\"notes\">{Enc(session.Notes)}</p>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string Enc(string? value)
        => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Fmt(DateTime dt)
        => dt.Kind == DateTimeKind.Utc
            ? dt.ToString("u")
            : dt.ToUniversalTime().ToString("u");

    private static string StatusClass(TestStatus status) => status switch
    {
        TestStatus.Passed => "pass",
        TestStatus.Failed => "fail",
        TestStatus.Warning or TestStatus.Unsupported => "warn",
        TestStatus.Cancelled => "cancel",
        TestStatus.NotRun => "na",
        _ => "na",
    };
}
