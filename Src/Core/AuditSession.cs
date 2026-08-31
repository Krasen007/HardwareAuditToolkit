using System.Text.Json.Serialization;

namespace HardwareAuditToolkit.Core;

/// <summary>
/// Aggregates all data from a single audit session.
/// </summary>
public class AuditSession
{
    /// <summary>
    /// Hostname of the machine being audited.
    /// </summary>
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the session started.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Timestamp when the session ended.
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Unique identifier for this session.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// List of module results from this session.
    /// </summary>
    [JsonPropertyName("modules")]
    public List<ModuleResult> Modules { get; set; } = [];

    /// <summary>
    /// Overall session status.
    /// </summary>
    [JsonPropertyName("overallStatus")]
    public TestStatus OverallStatus { get; set; } = TestStatus.NotRun;

    /// <summary>
    /// Optional machine identifier (CPU ID, BIOS, etc.).
    /// </summary>
    [JsonPropertyName("machineId")]
    public string? MachineId { get; set; }

    /// <summary>
    /// Path to the generated HTML report.
    /// </summary>
    [JsonPropertyName("reportPath")]
    public string? ReportPath { get; set; }

    /// <summary>
    /// Path to the JSON session data file.
    /// </summary>
    [JsonPropertyName("jsonPath")]
    public string? JsonPath { get; set; }
}

/// <summary>
/// Result of a single module execution.
/// </summary>
public class ModuleResult
{
    /// <summary>
    /// Module identifier.
    /// </summary>
    [JsonPropertyName("moduleId")]
    public string ModuleId { get; set; } = string.Empty;

    /// <summary>
    /// Module display name.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Final status of the module.
    /// </summary>
    [JsonPropertyName("status")]
    public TestStatus Status { get; set; } = TestStatus.NotRun;

    /// <summary>
    /// Timestamp when the module started.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Timestamp when the module completed.
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Any warnings or findings from the module.
    /// </summary>
    [JsonPropertyName("findings")]
    public List<string> Findings { get; set; } = [];

    /// <summary>
    /// Live measurements collected during the module run.
    /// </summary>
    [JsonPropertyName("measurements")]
    public List<ModuleMeasurement> Measurements { get; set; } = [];

    /// <summary>
    /// Operator actions logged during the module.
    /// </summary>
    [JsonPropertyName("operatorActions")]
    public List<string> OperatorActions { get; set; } = [];
}

/// <summary>
/// A live measurement from a module, with additional context.
/// </summary>
public class ModuleMeasurement
{
    /// <summary>
    /// Timestamp of the measurement.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// The measurement value.
    /// </summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>
    /// Label/description of what was measured.
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Additional context (e.g., which key, which core, etc.)
    /// </summary>
    [JsonPropertyName("context")]
    public string? Context { get; set; }
}