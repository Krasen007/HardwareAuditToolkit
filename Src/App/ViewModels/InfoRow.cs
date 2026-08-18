namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>One label/value row in a details grid.</summary>
public sealed class InfoRow
{
    public InfoRow(string label, string? value, string? group = null)
    {
        Label = label;
        Value = value;
        Group = group;
    }

    public string Label { get; }
    public string? Value { get; }
    public string? Group { get; }
}

/// <summary>A single live sensor reading, formatted for display.</summary>
public sealed class SensorRow
{
    public SensorRow(string hardware, string sensor, string? value)
    {
        Hardware = hardware;
        Sensor = sensor;
        Value = value;
    }

    public string Hardware { get; }
    public string Sensor { get; }
    public string? Value { get; }

    public string Display => $"{Sensor}: {Value}";
}
