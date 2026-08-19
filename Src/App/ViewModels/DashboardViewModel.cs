using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareAuditToolkit.App.Services;

namespace HardwareAuditToolkit.App.ViewModels;

public sealed partial class DashboardViewModel(INavigationService navigation) : ObservableObject
{
    public ObservableCollection<DashboardItemViewModel> Modules { get; } =
    [
        new DashboardItemViewModel("keyboard", "Keyboard Test", "Per-key coverage, WPM and accuracy.", "keyboard", true, navigation),
        new DashboardItemViewModel("mouse", "Mouse Test", "Click/scroll/drag log and tracing accuracy.", "mouse", true, navigation),
        new DashboardItemViewModel("monitor", "Monitor Test", "Fullscreen patterns and DDC/CI brightness.", "monitor", true, navigation),
        new DashboardItemViewModel("system", "System Info", "WMI/CIM inventory: CPU, RAM, disk, BIOS.", "system", false, navigation),
        new DashboardItemViewModel("stress", "CPU Stress Test", "Fixed-duration burn-in across all cores.", "stress", true, navigation),
    ];

    [RelayCommand]
    private void Back()
        => navigation.NavigateToDashboard();
}
