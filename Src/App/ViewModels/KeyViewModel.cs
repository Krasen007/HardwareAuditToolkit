using CommunityToolkit.Mvvm.ComponentModel;
using HardwareAuditToolkit.App.Keyboard;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// Observable tile for one physical key in the keyboard test grid. Pixel
/// coordinates are precomputed from <see cref="KeyboardLayout"/> so the view can
/// place tiles on a canvas without recomputing geometry.
/// </summary>
public sealed partial class KeyViewModel : ObservableObject
{
    public int Id { get; }

    public string Label { get; }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    [ObservableProperty]
    private KeyState _state = KeyState.Untested;

    public KeyViewModel(int id, string label, double x, double y, double width, double height)
    {
        Id = id;
        Label = label;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}
