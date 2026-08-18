using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.App.Messages;
using HardwareAuditToolkit.App.Services;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// Root view model for the shell window. Hosts the persistent header (with the
/// always-available Exit Test command) and the current screen content.
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

    public ShellViewModel()
    {
        // CurrentScreen is populated by the bootstrap once Navigation is wired.
    }

    /// <summary>
    /// Mouse-only, always-available exit path. Routed through the same flow as
    /// Ctrl+E and the native window close (§6).
    /// </summary>
    [RelayCommand]
    private void Exit()
        => WeakReferenceMessenger.Default.Send(new ExitRequestedMessage());

    public void ShowDashboard()
        => Navigation.NavigateToDashboard();
}
