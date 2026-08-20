using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using HardwareAuditToolkit.Core.Keyboard;

namespace HardwareAuditToolkit.App.Converters;

/// <summary>
/// Maps a key's <see cref="KeyState"/> to a tile background brush for the
/// keyboard test grid.
/// </summary>
public sealed class KeyStateToBrushConverter : IValueConverter
{
    private static readonly Brush Untested = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));
    private static readonly Brush Pressed = new SolidColorBrush(Color.FromRgb(0x27, 0x63, 0x2A));
    private static readonly Brush Confirmed = new SolidColorBrush(Color.FromRgb(0x1E, 0x5E, 0x8C));

    public object? Convert(object? value, System.Type? targetType, object? parameter, CultureInfo? culture)
    {
        return value is KeyState state
            ? state switch
            {
                KeyState.Pressed => Pressed,
                KeyState.Confirmed => Confirmed,
                _ => Untested,
            }
            : Untested;
    }

    public object? ConvertBack(object? value, System.Type? targetType, object? parameter, CultureInfo? culture)
        => throw new System.NotSupportedException();
}
