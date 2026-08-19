using CommunityToolkit.Mvvm.ComponentModel;
using HardwareAuditToolkit.App.Services;

namespace HardwareAuditToolkit.App.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentScreen;

    public INavigationService Navigation { get; set; } = null!;

    public Services.DeviceChangeService DeviceChange { get; set; } = null!;

    public ShellViewModel()
    {
    }

    public void ShowDashboard()
        => Navigation.NavigateToDashboard();
}
