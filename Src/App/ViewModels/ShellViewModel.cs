using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.App.Messages;
using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.App.Views;
using HardwareAuditToolkit.Core.Reporting;
using System.Windows;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// Root view model for the shell window. Hosts the persistent header (with the
/// always-available Exit Test command and the always-available Export Report command)
/// and the current screen content.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentScreen;

    /// <summary>Set by the bootstrap once the navigation service exists (resolves a
    /// shell ↔ navigation cyclic dependency).</summary>
    public INavigationService Navigation { get; set; } = null!;

    /// <summary>Set by the bootstrap so the dashboard can show live device counts.</summary>
    public Services.DeviceChangeService DeviceChange { get; set; } = null!;

    private readonly ReportExportService _reportExport;

    public ShellViewModel(ReportExportService reportExport)
    {
        _reportExport = reportExport ?? throw new ArgumentNullException(nameof(reportExport));
        // CurrentScreen is populated by the bootstrap once Navigation is wired.
    }

    /// <summary>
    /// Mouse-only, always-available exit path. Routed through the same flow as
    /// Ctrl+E and the native window close (§6).
    /// </summary>
    [RelayCommand]
    private void Exit()
        => WeakReferenceMessenger.Default.Send(new ExitRequestedMessage());

    /// <summary>
    /// Mouse-only, always-available export path (§10 Phase 6). Runs the full
    /// write-path fallback cascade and reports the outcome to the operator.
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

    public void ShowDashboard()
        => Navigation.NavigateToDashboard();
}
