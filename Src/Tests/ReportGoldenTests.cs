using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Core.Reporting;
using Xunit;

namespace HardwareAuditToolkit.Tests;

/// <summary>
/// Roadmap C8: locks the HTML and JSON artifacts against drift with golden files for four
/// deliberately-shaped sessions (empty, one-module, mid-run, full-with-defect), plus an
/// escaping test proving a hostile hostname or finding cannot inject markup. All scenarios
/// use fixed timestamps and a fixed time zone so the output is deterministic across machines.
/// </summary>
public class ReportGoldenTests
{
    private const string GoldenDir = "Golden";

    private static readonly DateTime Started = new(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Completed = new(2026, 8, 19, 10, 30, 0, DateTimeKind.Utc);

    private static List<ITestModule> Roster() =>
    [
        new RosterModule("keyboard", "Keyboard Test"),
        new RosterModule("mouse", "Mouse Test"),
        new RosterModule("monitor", "Monitor Test"),
        new RosterModule("system", "System Info"),
        new RosterModule("stress", "CPU Stress Test"),
    ];

    private static AuditSession Base() => new()
    {
        SessionId = "00000000000000000000000000000001",
        Hostname = "GOLDENHOST",
        MachineId = "10000000-0000-0000-0000-000000000001",
        StartedAt = Started,
        CompletedAt = Completed,
    };

    private static ModuleResult StartedModule(string id, string display, TestStatus status)
        => new()
        {
            ModuleId = id,
            DisplayName = display,
            Status = status,
            StartedAt = Started,
            CompletedAt = status == TestStatus.Running ? null : new DateTime(2026, 8, 19, 10, 5, 0, DateTimeKind.Utc),
        };

    private static ReportModel BuildModel(string scenario)
    {
        var session = Base();
        switch (scenario)
        {
            case "empty":
                // No module ever started.
                break;
            case "one-module":
                session.Modules.Add(StartedModule("keyboard", "Keyboard Test", TestStatus.Passed));
                session.Modules[0].Findings.Add("Operator confirmed: every expected key registered at least once.");
                break;
            case "mid-run":
                session.Modules.Add(StartedModule("keyboard", "Keyboard Test", TestStatus.Running));
                break;
            case "full-with-defect":
                session.Modules.Add(StartedModule("keyboard", "Keyboard Test", TestStatus.Passed));
                session.Modules[0].Findings.Add("Operator confirmed: every expected key registered at least once.");
                session.Modules.Add(StartedModule("mouse", "Mouse Test", TestStatus.Passed));
                session.Modules[1].Findings.Add("Operator confirmed. Clicks — L:3 R:1 M:0; wheel ticks:5; drags:1.");
                session.Modules.Add(StartedModule("monitor", "Monitor Test", TestStatus.Failed));
                session.Modules[2].Findings.Add("Operator flagged a dead pixel in the lower-left corner.");
                session.Modules.Add(StartedModule("system", "System Info", TestStatus.Passed));
                session.Modules[3].Findings.Add("Inventory captured for GOLDENHOST: Intel ABC, 16 GB, 2 fixed disk(s).");
                session.Modules.Add(StartedModule("stress", "CPU Stress Test", TestStatus.Passed));
                session.Modules[4].Findings.Add("Burn-in completed the full target duration of 00:05:00.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        // Deterministic: convert in UTC so "local" display equals the UTC wall time.
        return ReportModelFactory.Build(session, Roster(), TimeZoneInfo.Utc);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("one-module")]
    [InlineData("mid-run")]
    [InlineData("full-with-defect")]
    public void GoldenScenario_MatchesCheckedInHtml(string scenario)
    {
        string html = new HtmlReportTemplate().Render(BuildModel(scenario));
        string golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, GoldenDir, scenario + ".html"));
        Assert.Equal(Normalize(golden), Normalize(html));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("one-module")]
    [InlineData("mid-run")]
    [InlineData("full-with-defect")]
    public void GoldenScenario_MatchesCheckedInJson(string scenario)
    {
        string json = ReportJsonSerializer.Serialize(BuildModel(scenario));
        string golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, GoldenDir, scenario + ".json"));
        Assert.Equal(Normalize(golden), Normalize(json));
    }

    /// <summary>
    /// A hostile hostname or finding must not be able to inject markup into the report.
    /// Everything interpolated into the document passes through HTML encoding.
    /// </summary>
    [Fact]
    public void HtmlReport_EscapesHostileHostnameAndFinding()
    {
        var session = Base();
        session.Hostname = "<script>alert('host')</script>\" & <b>";
        session.Modules.Add(StartedModule("keyboard", "Keyboard Test", TestStatus.Passed));
        session.Modules[0].Findings.Add("<script>alert('finding')</script>");
        session.Modules[0].OperatorActions.Add("<img src=x onerror=alert(1)>");

        string html = new HtmlReportTemplate().Render(ReportModelFactory.Build(session, Roster(), TimeZoneInfo.Utc));

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img ", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;b&gt;", html);
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n");

    private sealed class RosterModule : ITestModule
    {
        public RosterModule(string id, string displayName) { ModuleId = id; Metadata = new RosterMetadata(id, displayName); }
        public IModuleMetadata Metadata { get; }
        public string ModuleId { get; }
        public ModulePhase CurrentPhase => ModulePhase.NotStarted;
        public bool IsRunning => false;
        public IList<ModuleMeasurement> Measurements { get; } = [];
        public IList<string> Findings { get; } = [];
        public IList<string> OperatorActions { get; } = [];
        public bool CheckPreconditions() => true;
        public void Start(Action<TestStatus> onComplete) { }
        public void Cancel() { }
    }

    private sealed class RosterMetadata : IModuleMetadata
    {
        public RosterMetadata(string id, string name) { Id = id; DisplayName = name; }
        public string Id { get; }
        public string DisplayName { get; }
        public string Description => string.Empty;
        public string Category => string.Empty;
        public string[] RequiredCapabilities => [];
        public bool IsExclusive => false;
        public TimeSpan? MaxDuration => null;
    }
}