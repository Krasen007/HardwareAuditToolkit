namespace HardwareAuditToolkit.App.Services;

/// <summary>
/// Drives screen transitions for the shell (dashboard ↔ module screens). Keeps
/// the "current screen" on <see cref="ViewModels.ShellViewModel"/> so the
/// MainWindow content region re-renders via data templates.
/// </summary>
public interface INavigationService
{
    void NavigateToDashboard();
    void NavigateToModule(string moduleId);
}
