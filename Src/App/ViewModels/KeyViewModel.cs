using CommunityToolkit.Mvvm.ComponentModel;
using HardwareAuditToolkit.Core.Keyboard;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// Observable tile for one physical key in the keyboard test grid. Pixel
/// coordinates are precomputed from <see cref="KeyboardLayout"/> so the view can
/// place tiles on a canvas without recomputing geometry.
/// </summary>
public sealed partial class KeyViewModel(int id, string label, double x, double y, double width, double height) : ObservableObject
{
    public int Id { get; } = id;

    public string Label { get; } = label;

    public double X { get; } = x;

    public double Y { get; } = y;

    public double Width { get; } = width;

    public double Height { get; } = height;

    [ObservableProperty]
    private KeyState _state = KeyState.Untested;
}
