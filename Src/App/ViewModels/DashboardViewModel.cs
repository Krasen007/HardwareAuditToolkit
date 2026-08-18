using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareAuditToolkit.App.Services;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// Dashboard showing the planned audit modules. Live device topology is bound
/// directly to the singleton <see cref="Services.DeviceChangeService"/> via the
/// shell, so there is no per-navigation event subscription to leak (§9.5).
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly INavigationService _navigation;

    public ObservableCollection<DashboardItemViewModel> Modules { get; }

    public DashboardViewModel(INavigationService navigation)
    {
        _navigation = navigation;

        Modules =
        [
            new DashboardItemViewModel("keyboard", "Keyboard Test", "Per-key coverage, WPM and accuracy.", "keyboard", true, navigation),
            new DashboardItemViewModel("mouse", "Mouse Test", "Click/scroll/drag log and tracing accuracy.", "mouse", true, navigation),
            new DashboardItemViewModel("monitor", "Monitor Test", "Fullscreen patterns and DDC/CI brightness.", "monitor", true, navigation),
            new DashboardItemViewModel("system", "System Info", "WMI/CIM inventory: CPU, RAM, disk, BIOS.", "system", false, navigation),
            new DashboardItemViewModel("stress", "CPU Stress Test", "Fixed-duration burn-in across all cores.", "stress", true, navigation),
        ];
    }

    [RelayCommand]
    private void Back()
        => _navigation.NavigateToDashboard();
}
