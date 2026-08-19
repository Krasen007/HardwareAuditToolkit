using System.Text.Json;

namespace HardwareAuditToolkit.Core.Reporting;

/// <summary>
/// Serializes an <see cref="AuditSession"/> to a structured JSON file plus a human-readable
/// HTML report, and persists them through the write-path fallback cascade (architecture
/// §9.6). The cascade is deliberately resilient: each candidate location is probed with a
/// quick write-test before the real payload is written, and any failure — including a
/// volume vanishing mid-write (e.g. the USB stick pulled) — is caught so the export moves
/// on to the next candidate instead of losing the in-memory session data.
///
/// Pure logic only: no UI, no Win32, no file dialogs. Those are supplied by the caller via
/// <see cref="ReportExportOptions"/> so this class stays unit-testable in isolation.
/// </summary>
public sealed class SessionExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IReportTemplate _template;

    public SessionExporter() : this(new HtmlReportTemplate())
    {
    }

    public SessionExporter(IReportTemplate template)
    {
        _template = template ?? throw new ArgumentNullException(nameof(template));
    }

    /// <summary>
    /// Exports the session. On success sets <see cref="AuditSession.JsonPath"/> and
    /// <see cref="AuditSession.ReportPath"/> on the supplied session. The session object is
    /// otherwise not mutated (lifecycle completion is owned by the orchestrator). On the
    /// clipboard fallback path no file is written but <see cref="ReportExportResult.Success"/>
    /// is still <c>true</c> because the data was preserved.
    /// </summary>
    public ReportExportResult Export(AuditSession session, ReportExportOptions options)
    {
        if (session is null)
            throw new ArgumentNullException(nameof(session));
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        string json = JsonSerializer.Serialize(session, JsonOptions);
        string html = _template.Render(session);
        string baseName = BuildBaseName(session);

        foreach (var dir in options.PreferredDirectories ?? Array.Empty<string>())
        {
            if (TryWritePair(dir, baseName, json, html, out var jsonPath, out var htmlPath))
            {
                return Succeed(session, jsonPath!, htmlPath!, "Audit report saved.");
            }
        }

        // Step 4 (§9.6): manual folder picker. A null return means the operator cancelled;
        // we still fall through to the clipboard last resort rather than losing the data.
        if (options.RequestManualFolder is { } picker)
        {
            var dir = picker();
            if (dir is not null && TryWritePair(dir, baseName, json, html, out var jsonPath, out var htmlPath))
            {
                return Succeed(session, jsonPath!, htmlPath!, "Audit report saved to the chosen folder.");
            }
        }

        // Step 5 (§9.6): last resort — copy JSON to clipboard so the audit is never lost.
        if (options.ShowClipboardFallback is { } clipboard && clipboard(json))
        {
            return new ReportExportResult
            {
                Success = true,
                JsonContent = json,
                FailureReason = ExportFailureReason.None,
                Message = "No writable location found; the audit JSON was copied to the clipboard.",
            };
        }

        return new ReportExportResult
        {
            Success = false,
            FailureReason = ExportFailureReason.NoWritableLocation,
            Message = "The audit report could not be written to any location.",
        };
    }

    /// <summary>
    /// Attempts a quick write-test followed by the real JSON + HTML write into
    /// <paramref name="directory"/>. Returns false (cleaning up any partial files) on any
    /// failure, including a volume disappearing between the test and the payload write.
    /// </summary>
    private static bool TryWritePair(string? directory, string baseName, string json, string html, out string? jsonPath, out string? htmlPath)
    {
        jsonPath = null;
        htmlPath = null;

        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            string resolved = directory!;
            Directory.CreateDirectory(resolved);

            // Quick write-test so an absent/unwritable volume is caught before the payload.
            string testFile = Path.Combine(resolved, $".hat_writetest_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "ok");
            File.Delete(testFile);

            jsonPath = Path.Combine(resolved, baseName + ".json");
            htmlPath = Path.Combine(resolved, baseName + ".html");
            File.WriteAllText(jsonPath, json);
            File.WriteAllText(htmlPath, html);
            return true;
        }
        catch
        {
            SafeDelete(jsonPath);
            SafeDelete(htmlPath);
            jsonPath = null;
            htmlPath = null;
            return false;
        }
    }

    private static void SafeDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort; the next candidate will overwrite or the file is left partial.
        }
    }

    private static ReportExportResult Succeed(AuditSession session, string jsonPath, string htmlPath, string message)
    {
        session.JsonPath = jsonPath;
        session.ReportPath = htmlPath;
        return new ReportExportResult
        {
            Success = true,
            JsonPath = jsonPath,
            HtmlPath = htmlPath,
            FailureReason = ExportFailureReason.None,
            Message = message,
        };
    }

    private static string BuildBaseName(AuditSession session)
    {
        string host = string.IsNullOrWhiteSpace(session.Hostname) ? "unknown" : session.Hostname;
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            host = host.Replace(c, '_');
        }

        DateTime stamp = session.StartedAt == default ? DateTime.UtcNow : session.StartedAt;
        return $"{host}_{stamp:yyyyMMddHHmmss}";
    }
}
