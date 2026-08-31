using System.Text.Json;

namespace HardwareAuditToolkit.Core.Reporting;

/// <summary>
/// Serializes a <see cref="ReportModel"/> to the JSON artifact. Exposed as a static so the
/// writers (the cascade in <see cref="SessionExporter"/> and the golden-file tests) share
/// exactly one serialization, preventing drift between what is written and what is tested.
/// </summary>
public static class ReportJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(ReportModel model)
        => JsonSerializer.Serialize(model ?? throw new ArgumentNullException(nameof(model)), Options);
}