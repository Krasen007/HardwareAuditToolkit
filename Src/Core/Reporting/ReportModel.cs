using System.Text.Json.Serialization;

namespace HardwareAuditToolkit.Core.Reporting;

/// <summary>
/// Reader-facing model of one audit, decoupled from the in-memory
/// <see cref="AuditSession"/>. Both output writers (HTML and JSON) render this one
/// deliberate shape, so raw enum identifiers, internal measurement-context tags and
/// .NET exception type names are mapped to display text here instead of leaking to
/// the person who has to trust the report (architecture principle 5).
/// </summary>
public sealed class ReportModel
{
    /// <summary>Report contract version (roadmap E4). Bump on any breaking change to
    /// the JSON shape so downstream readers can detect what they are parsing.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("hostname")]
    public string Hostname { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    /// <summary>Session start, UTC.</summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; init; }

    /// <summary>Session end, UTC. Null while the session is still in progress.</summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; init; }

    [JsonPropertyName("machineId")]
    public string? MachineId { get; init; }

    /// <summary>Decided status, already in display form ("Passed", "Not run",
    /// "Partial — not all modules were tested", …).</summary>
    [JsonPropertyName("overallStatus")]
    public string OverallStatus { get; init; } = string.Empty;

    [JsonPropertyName("statusCounts")]
    public ReportStatusCounts Counts { get; init; } = new();

    [JsonPropertyName("reportPath")]
    public string? ReportPath { get; init; }

    [JsonPropertyName("jsonPath")]
    public string? JsonPath { get; init; }

    [JsonPropertyName("modules")]
    public List<ReportModuleResult> Modules { get; init; } = [];

    /// <summary>Started-at formatted in the technician's local time zone.</summary>
    [JsonIgnore]
    public string StartedAtLocal { get; init; } = string.Empty;

    /// <summary>Completed-at formatted in the technician's local time zone
    /// (<c>null</c> while in progress).</summary>
    [JsonIgnore]
    public string? CompletedAtLocal { get; init; }
}

/// <summary>Aggregate module status counts used to lead the report and the JSON.</summary>
public sealed class ReportStatusCounts
{
    [JsonPropertyName("passed")] public int Passed { get; init; }
    [JsonPropertyName("failed")] public int Failed { get; init; }
    [JsonPropertyName("warning")] public int Warning { get; init; }
    [JsonPropertyName("cancelled")] public int Cancelled { get; init; }
    [JsonPropertyName("running")] public int Running { get; init; }
    [JsonPropertyName("notRun")] public int NotRun { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
}

/// <summary>A single module's report section. Status is a display string, never a raw
/// <see cref="TestStatus"/> identifier.</summary>
public sealed class ReportModuleResult
{
    [JsonPropertyName("moduleId")]
    public string ModuleId { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; init; }

    [JsonPropertyName("findings")]
    public List<string> Findings { get; init; } = [];

    [JsonPropertyName("operatorActions")]
    public List<string> OperatorActions { get; init; } = [];

    [JsonPropertyName("measurements")]
    public List<ReportMeasurement> Measurements { get; init; } = [];
}

/// <summary>A measurement destined for the report. Carries no raw internal context tag;
/// the caller chooses a reader-facing label.</summary>
public sealed class ReportMeasurement
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string? Value { get; init; }
}

/// <summary>Maps a raw <see cref="TestStatus"/> to the display name a reader should see.
/// Centralised here so neither writer can accidentally emit a raw enum identifier.</summary>
public static class StatusDisplay
{
    public static string Name(TestStatus s) => s switch
    {
        TestStatus.NotRun => "Not run",
        TestStatus.Running => "Running",
        TestStatus.Passed => "Passed",
        TestStatus.Failed => "Failed",
        TestStatus.Warning => "Warning",
        TestStatus.Cancelled => "Cancelled",
        _ => s.ToString(),
    };
}