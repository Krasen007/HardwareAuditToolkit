using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.App.Views;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Core.Messages;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// The window shell: hosts the current screen, drives the persistent header
/// (roadmap E2 — Back / Export Report / Exit Test), which replaces the per-view
/// copies of the exit overlay and dashboard button.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentScreen;

    /// <summary>False while a module screen is open (drives the header's Back button).</summary>
    [ObservableProperty]
    private bool _isDashboard = true;

    public INavigationService Navigation { get; set; } = null!;

    public ReportExportService ReportExport { get; set; } = null!;

    public Services.DeviceChangeService DeviceChange { get; set; } = null!;

    public ShellViewModel()
    {
    }

    public void ShowDashboard()
        => Navigation.NavigateToDashboard();

    [RelayCommand]
    private void Back()
        => Navigation.NavigateToDashboard();

        /// <summary>
    /// The mouse twin of Ctrl+E (§6): sends <see cref="ExitRequestedMessage"/>, which
    /// App routes to the orchestrator's abort path (records
    /// <see cref="TestStatus.Cancelled"/>) and returns to the dashboard. This is the
    /// one deliberate abort; navigating away records nothing (roadmap Phase 2).
    /// </summary>
    [RelayCommand]
    private void ExitTest()
        => WeakReferenceMessenger.Default.Send(new ExitRequestedMessage());

    /// <summary>
    /// Runs the §9.6 export cascade for the current session, then shows the outcome. A
    /// hard failure is surfaced explicitly instead of showing the operator nothing
    /// (roadmap C6); any partial success (file or clipboard) opens the result dialog.
    /// </summary>
    [RelayCommand]
    private void ExportReport()
    {
        var result = ReportExport.Export();
        if (!result.Success)
        {
            System.Windows.MessageBox.Show(
                result.Message ?? "The audit report could not be written to any location.",
                "Export Failed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return;
        }

        ExportResultDialog.ShowResult(result);
    }
}
