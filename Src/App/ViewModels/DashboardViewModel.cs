using System.Collections.ObjectModel;
using System.Windows;
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
    /// Runs the §9.6 export cascade for the current session, then shows the outcome. A
    /// hard failure is surfaced explicitly instead of showing the operator nothing
    /// (roadmap C6); any partial success (file or clipboard) opens the result dialog.
    /// </summary>
    [RelayCommand]
    private void ExportReport()
    {
        var result = reportExport.Export();
        if (!result.Success)
        {
            MessageBox.Show(
                result.Message ?? "The audit report could not be written to any location.",
                "Export Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        ExportResultDialog.ShowResult(result);
    }
}
