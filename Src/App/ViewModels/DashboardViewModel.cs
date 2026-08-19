using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.App.Views;

namespace HardwareAuditToolkit.App.ViewModels;

public sealed partial class DashboardViewModel(INavigationService navigation, ReportExportService reportExport) : ObservableObject
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

    /// <summary>
    /// Runs the §9.6 export cascade for the current session, then shows the saved
    /// location when a file was written. The dashboard carries an always-available
    /// Export Report button (README Phase 6) so the report is reachable without
    /// completing any test module.
    /// </summary>
    [RelayCommand]
    private void ExportReport()
    {
        var result = reportExport.Export();
        if (result.Success && result.JsonPath is not null)
        {
            ExportResultDialog.ShowResult(result);
        }
    }
}
