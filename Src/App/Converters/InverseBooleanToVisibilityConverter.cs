using System.Globalization;
using System.Windows.Data;

namespace HardwareAuditToolkit.App.Converters;

/// <summary>
/// Inverts a boolean for <see cref="System.Windows.Visibility"/> binding (true →
/// <c>Collapsed</c>, false → <c>Visible</c>).
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, System.Type? targetType, object? parameter, CultureInfo? culture)
        => value is bool b ? (b ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible)
                            : System.Windows.Visibility.Visible;

    public object? ConvertBack(object? value, System.Type? targetType, object? parameter, CultureInfo? culture)
        => throw new System.NotSupportedException();
}
