using System.Net;
using System.Text;

namespace HardwareAuditToolkit.Core.Reporting;

/// <summary>
/// Default HTML report renderer (architecture §10 Phase 6). Self-contained, no external
/// dependencies: inline CSS, a summary table and counts, and a per-module detail section.
/// Designed to print cleanly to PDF from any browser. Renders exclusively from a
/// <see cref="ReportModel"/> so no raw enum identifier, internal context tag or exception
/// type name can reach the reader — those are already resolved to display text upstream.
/// </summary>
public sealed class HtmlReportTemplate : IReportTemplate
{
    public string Render(ReportModel model)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

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
        sb.AppendLine("    .counts { font-weight: 600; margin: 10px 0 0; font-size: 13px; }");
        sb.AppendLine("    table { border-collapse: collapse; width: 100%; font-size: 13px; }");
        sb.AppendLine("    th, td { border: 1px solid #ccc; padding: 6px 8px; text-align: left; vertical-align: top; }");
        sb.AppendLine("    th { background: #2D2D30; color: #fff; }");
        sb.AppendLine("    tr:nth-child(even) td { background: #f6f6f6; }");
        sb.AppendLine("    .status { font-weight: 600; }");
        sb.AppendLine("    .pass { color: #1e7e34; } .fail { color: #c0392b; } .warn { color: #b9770e; }");
        sb.AppendLine("    .na { color: #888; } .cancel { color: #6c3483; }");
        sb.AppendLine("    ul { margin: 4px 0 4px 18px; padding: 0; }");
        sb.AppendLine("    @media print { body { margin: 12px; } }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        sb.AppendLine("  <h1>Hardware Audit Report</h1>");
        sb.AppendLine("  <div class=\"meta\">");
        sb.AppendLine($"    <span><b>Host:</b> {Enc(model.Hostname)}</span>");
        sb.AppendLine($"    <span><b>Session:</b> {Enc(model.SessionId)}</span>");
        sb.AppendLine($"    <span><b>Started:</b> {Enc(Fmt(model.StartedAt))} (local {Enc(model.StartedAtLocal)})</span>");
        sb.AppendLine($"    <span><b>Completed:</b> {Enc(CompletedDisplay(model))}</span>");
        if (!string.IsNullOrEmpty(model.MachineId))
        {
            sb.AppendLine($"    <span><b>Machine ID:</b> {Enc(model.MachineId)}</span>");
        }

        sb.AppendLine("  </div>");
        sb.AppendLine($"  <p class=\"meta\">Overall status: <span class=\"status {StatusClass(model.OverallStatus)}\">{Enc(model.OverallStatus)}</span></p>");
        sb.AppendLine($"  <p class=\"counts\">{Enc(CountsPhrase(model.Counts))}</p>");

        // Summary table — every registered module, including ones never started.
        sb.AppendLine("  <h2>Modules</h2>");
        sb.AppendLine("  <table>");
        sb.AppendLine("    <thead><tr><th>Module</th><th>Status</th><th>Started</th><th>Completed</th></tr></thead>");
        sb.AppendLine("    <tbody>");
        foreach (var m in model.Modules)
        {
            sb.AppendLine("      <tr>");
            sb.AppendLine($"        <td>{Enc(m.DisplayName)}</td>");
            sb.AppendLine($"        <td class=\"status {StatusClass(m.Status)}\">{Enc(m.Status)}</td>");
            sb.AppendLine($"        <td>{Enc(m.StartedAt is { } ms ? Fmt(ms) : "-")}</td>");
            sb.AppendLine($"        <td>{Enc(m.CompletedAt is { } mc ? Fmt(mc) : "-")}</td>");
            sb.AppendLine("      </tr>");
        }
        sb.AppendLine("    </tbody>");
        sb.AppendLine("  </table>");

        return Finish(model, sb);
    }

    /// <summary>Appends the per-module detail sections and closes the document.</summary>
    private static string Finish(ReportModel model, StringBuilder sb)
    {
        foreach (var m in model.Modules)
        {
            sb.AppendLine($"  <h2>{Enc(m.DisplayName)} — {Enc(m.Status)}</h2>");

            if (m.Findings.Count > 0)
            {
                sb.AppendLine("  <p><b>Findings</b></p><ul>");
                foreach (var f in m.Findings)
                {
                    sb.AppendLine($"    <li>{Enc(f)}</li>");
                }

                sb.AppendLine("  </ul>");
            }

            if (m.OperatorActions.Count > 0)
            {
                sb.AppendLine("  <p><b>Operator actions</b></p><ul>");
                foreach (var a in m.OperatorActions)
                {
                    sb.AppendLine($"    <li>{Enc(a)}</li>");
                }

                sb.AppendLine("  </ul>");
            }

            if (m.Measurements.Count > 0)
            {
                sb.AppendLine("  <p><b>Measurements</b></p>");
                sb.AppendLine("  <table>");
                sb.AppendLine("    <thead><tr><th>Time</th><th>Label</th><th>Value</th></tr></thead>");
                sb.AppendLine("    <tbody>");
                foreach (var mm in m.Measurements)
                {
                    sb.AppendLine("      <tr>");
                    sb.AppendLine($"        <td>{Enc(Fmt(mm.Timestamp))}</td>");
                    sb.AppendLine($"        <td>{Enc(mm.Label)}</td>");
                    sb.AppendLine($"        <td>{Enc(mm.Value ?? "-")}</td>");
                    sb.AppendLine("      </tr>");
                }

                sb.AppendLine("    </tbody>");
                sb.AppendLine("  </table>");
            }
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string CompletedDisplay(ReportModel model)
    {
        if (model.CompletedAt is not { } completed)
        {
            return "in progress";
        }

        return model.CompletedAtLocal is { } local
            ? $"{Fmt(completed)} (local {local})"
            : Fmt(completed);
    }

    /// <summary>Renders the per-module status counts line, e.g. "3 passed, 1 failed, 1 not run."</summary>
    private static string CountsPhrase(ReportStatusCounts c)
    {
        if (c.Total == 0)
        {
            return "No modules were run in this session.";
        }

        var bits = new List<string>();
        void Add(int count, string label)
        {
            if (count > 0)
            {
                bits.Add($"{count} {label}");
            }
        }

        Add(c.Passed, "passed");
        Add(c.Failed, "failed");
        Add(c.Warning, "warning");
        Add(c.Cancelled, "cancelled");
        Add(c.Running, "in progress");
        Add(c.NotRun, "not run");

        return bits.Count == 0 ? "No modules were run in this session." : string.Join(", ", bits) + ".";
    }

    private static string Enc(string? value)
        => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Fmt(DateTime dt)
        => dt.Kind == DateTimeKind.Utc
            ? dt.ToString("u")
            : dt.ToUniversalTime().ToString("u");

    private static string StatusClass(string status) => status switch
    {
        "Passed" => "pass",
        "Failed" => "fail",
        "Warning" => "warn",
        "Cancelled" => "cancel",
        "Not run" => "na",
        _ => "na",
    };
}