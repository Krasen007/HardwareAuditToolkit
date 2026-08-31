using System.Text.Json;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Core.Reporting;
using Xunit;

namespace HardwareAuditToolkit.Tests;

public class ReportExportTests
{
    private static AuditSession BuildSession(TestStatus overall = TestStatus.Passed)
    {
        var session = new AuditSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Hostname = "TESTHOST",
            StartedAt = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 8, 19, 10, 30, 0, DateTimeKind.Utc),
            OverallStatus = overall,
        };
        session.Modules.Add(new ModuleResult
        {
            ModuleId = "keyboard",
            DisplayName = "Keyboard Test",
            Status = TestStatus.Passed,
            StartedAt = session.StartedAt,
            CompletedAt = session.CompletedAt,
            Findings = { "All keys registered." },
        });
        return session;
    }

    private static string InvalidDirectoryPath()
        => Path.Combine(Path.GetTempPath(), $"bad<{Guid.NewGuid():N}>dir");

    [Fact]
    public void Export_ToWritablePreferredDir_WritesJsonAndHtml()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hat_export_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var session = BuildSession();
            var exporter = new SessionExporter();

            var result = exporter.Export(session, new ReportExportOptions
            {
                PreferredDirectories = [dir],
            });

            Assert.True(result.Success);
            Assert.NotNull(result.JsonPath);
            Assert.NotNull(result.HtmlPath);
            Assert.True(File.Exists(result.JsonPath));
            Assert.True(File.Exists(result.HtmlPath));
            Assert.Equal(result.JsonPath, session.JsonPath);
            Assert.Equal(result.HtmlPath, session.ReportPath);
            Assert.Contains(".json", result.JsonPath);
            Assert.Contains(".html", result.HtmlPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Export_JsonRoundTripsReportModel()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hat_export_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var session = BuildSession();
            var exporter = new SessionExporter();

            var result = exporter.Export(session, new ReportExportOptions
            {
                PreferredDirectories = [dir],
            });

            string json = File.ReadAllText(result.JsonPath!);
            var round = JsonSerializer.Deserialize<ReportModel>(json);

            Assert.NotNull(round);
            Assert.Equal("TESTHOST", round!.Hostname);
            Assert.Equal("Passed", round.OverallStatus);
            Assert.Single(round.Modules);
            Assert.Equal("Passed", round.Modules[0].Status);
            Assert.Equal("All keys registered.", round.Modules[0].Findings[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Export_UnwritablePreferredDir_FallsBackToManualPicker()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hat_export_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var session = BuildSession();
            var exporter = new SessionExporter();
            bool pickerCalled = false;

            var result = exporter.Export(session, new ReportExportOptions
            {
                PreferredDirectories = [InvalidDirectoryPath()],
                RequestManualFolder = () =>
                {
                    pickerCalled = true;
                    return dir;
                },
            });

            Assert.True(result.Success);
            Assert.True(pickerCalled);
            Assert.True(File.Exists(result.JsonPath));
            Assert.Equal(dir, Path.GetDirectoryName(result.JsonPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Export_NoPreferredAndPickerCancelled_FallsBackToClipboard()
    {
        var session = BuildSession();
        var exporter = new SessionExporter();
        bool clipboardCalled = false;
        string? clipboardJson = null;

        var result = exporter.Export(session, new ReportExportOptions
        {
            PreferredDirectories = [InvalidDirectoryPath()],
            RequestManualFolder = () => null,
            ShowClipboardFallback = json =>
            {
                clipboardCalled = true;
                clipboardJson = json;
                return true;
            },
        });

        Assert.True(result.Success);
        Assert.True(clipboardCalled);
        Assert.NotNull(result.JsonContent);
        Assert.Equal(clipboardJson, result.JsonContent);
        Assert.Null(result.JsonPath);
        Assert.Null(result.HtmlPath);
        // Session is never mutated with file paths when only clipboard succeeded.
        Assert.Null(session.JsonPath);
        Assert.Null(session.ReportPath);
    }

    [Fact]
    public void Export_NoLocationAndNoFallback_Fails()
    {
        var session = BuildSession();
        var exporter = new SessionExporter();

        var result = exporter.Export(session, new ReportExportOptions
        {
            PreferredDirectories = [InvalidDirectoryPath()],
            RequestManualFolder = null,
            ShowClipboardFallback = null,
        });

        Assert.False(result.Success);
        Assert.Equal(ExportFailureReason.NoWritableLocation, result.FailureReason);
    }

    [Fact]
    public void Export_NullSessionOrOptions_Throws()
    {
        var exporter = new SessionExporter();
        Assert.Throws<ArgumentNullException>(() => exporter.Export(null!, new ReportExportOptions()));
        Assert.Throws<ArgumentNullException>(() => exporter.Export(new AuditSession(), null!));
    }

    [Fact]
    public void HtmlReportTemplate_RendersHostnameAndModule()
    {
        var session = BuildSession();
        var model = ReportModelFactory.Build(session);
        var html = new HtmlReportTemplate().Render(model);

        Assert.Contains("TESTHOST", html);
        Assert.Contains("Keyboard Test", html);
        Assert.Contains("<html", html);
        Assert.Contains("All keys registered.", html);
    }
}
