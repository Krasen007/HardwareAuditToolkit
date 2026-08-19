namespace HardwareAuditToolkit.Core.Reporting;

/// <summary>Outcome of a <see cref="SessionExporter.Export"/> attempt.</summary>
public sealed class ReportExportResult
{
    /// <summary>True when the session was persisted somewhere (file or clipboard).</summary>
    public bool Success { get; init; }

    /// <summary>Path of the written JSON file, when a file was written.</summary>
    public string? JsonPath { get; init; }

    /// <summary>Path of the written HTML report, when a file was written.</summary>
    public string? HtmlPath { get; init; }

    /// <summary>
    /// The serialized JSON, populated only on the clipboard-fallback path
    /// (§9.6 step 5) so the caller can surface it to the operator.
    /// </summary>
    public string? JsonContent { get; init; }

    /// <summary>Why the export failed, when <see cref="Success"/> is false.</summary>
    public ExportFailureReason FailureReason { get; init; }

    /// <summary>Human-readable summary suitable for a toast/modal.</summary>
    public string? Message { get; init; }
}

/// <summary>Why an export did not produce a file.</summary>
public enum ExportFailureReason
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>Every candidate location (including manual picker) was unwritable.</summary>
    NoWritableLocation,

    /// <summary>The operator cancelled the export.</summary>
    UserCancelled,
}
