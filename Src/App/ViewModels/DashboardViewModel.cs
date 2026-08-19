using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.App.Views;
using HardwareAuditToolkit.Core.Reporting;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// Dashboard showing the planned audit modules. Live device topology is bound
/// directly to the singleton <see cref="Services.DeviceChangeService"/> via the
/// shell, so there is no per-navigation event subscription to leak (§9.5).
/// </summary>
public sealed partial class DashboardViewModel(INavigationService navigation, ReportExportService reportExport) : ObservableObject
{
    private readonly ReportExportService _reportExport = reportExport;

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
    /// Exports the audit report via the full write-path fallback cascade (§10 Phase 6, §9.6).
    /// </summary>
    [RelayCommand]
    private void Export()
    {
        ReportExportResult result = _reportExport.Export();
        if (result.Success)
        {
            Views.ExportResultDialog.ShowResult(result);
        }
        else
        {
            MessageBox.Show(
                result.Message ?? "The audit report could not be exported.",
                "Export failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
