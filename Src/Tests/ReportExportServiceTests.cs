using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Core.Reporting;
using Xunit;

namespace HardwareAuditToolkit.Tests;

/// <summary>
/// App-layer tests for <see cref="ReportExportService"/> (architecture §10 Phase 6, §9.6).
/// The UI-bound cascade steps (manual folder picker, clipboard modal) are only reached when
/// every preferred directory is unwritable, so the happy path exercises the real cascade
/// without touching WPF dialogs or the clipboard.
/// </summary>
public class ReportExportServiceTests
{
    [Fact]
    public void Export_WritesJsonAndHtml_ToFirstWritableLocation_AndStampsSession()
    {
        var session = new AuditSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Hostname = "TESTHOST",
            StartedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        };
        Assert.Null(session.CompletedAt);

        var service = new ReportExportService(new SessionExporter(), session);

        var result = service.Export();

        Assert.True(result.Success);
        Assert.NotNull(result.JsonPath);
        Assert.NotNull(result.HtmlPath);
        Assert.EndsWith(".json", result.JsonPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".html", result.HtmlPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.JsonPath));
        Assert.True(File.Exists(result.HtmlPath));
        Assert.NotNull(session.CompletedAt);

        // §9.6 cascade preference: the first candidate is the app directory (next to the exe),
        // so a writable location writes there rather than falling through to Desktop/%TEMP%.
        string expectedDir = AppDirectory();
        Assert.Equal(expectedDir, Path.GetDirectoryName(result.JsonPath));

        // Both artifacts carry the machine identity and are well-formed.
        string json = File.ReadAllText(result.JsonPath!);
        Assert.Contains("\"hostname\": \"TESTHOST\"", json);

        string html = File.ReadAllText(result.HtmlPath!);
        Assert.Contains("<!DOCTYPE html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TESTHOST", html);

        File.Delete(result.JsonPath!);
        File.Delete(result.HtmlPath!);
    }

    [Fact]
    public void Export_DoesNotOverwriteAnAlreadySetCompletedAt()
    {
        var completed = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var session = new AuditSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Hostname = "KEEPHOST",
            StartedAt = DateTime.UtcNow,
            CompletedAt = completed,
        };

        var service = new ReportExportService(new SessionExporter(), session);

        var result = service.Export();

        Assert.True(result.Success);
        Assert.Equal(completed, session.CompletedAt);

        if (result.JsonPath is not null)
        {
            File.Delete(result.JsonPath);
        }

        if (result.HtmlPath is not null)
        {
            File.Delete(result.HtmlPath);
        }
    }

    private static string AppDirectory()
    {
        string? appDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrEmpty(appDir))
        {
            appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return appDir!;
    }
}
