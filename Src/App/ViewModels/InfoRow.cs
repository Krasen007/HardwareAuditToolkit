namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>One label/value row in a details grid.</summary>
public sealed class InfoRow(string label, string? value, string? group = null)
{
    public string Label { get; } = label;
    public string? Value { get; } = value;
    public string? Group { get; } = group;
}

/// <summary>A single live sensor reading, formatted for display.</summary>
public sealed class SensorRow(string hardware, string sensor, string? value)
{
    public string Hardware { get; } = hardware;
    public string Sensor { get; } = sensor;
    public string? Value { get; } = value;

    public string Display => $"{Sensor}: {Value}";
}
