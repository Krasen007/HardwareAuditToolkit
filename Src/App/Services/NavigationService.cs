using HardwareAuditToolkit.App.ViewModels;

namespace HardwareAuditToolkit.App.Services;

public sealed class NavigationService : INavigationService
{
    private readonly ShellViewModel _shell;

    public NavigationService(ShellViewModel shell)
    {
        _shell = shell;
    }

    public void NavigateToDashboard()
        => _shell.CurrentScreen = new DashboardViewModel(this);

    public void NavigateToModule(string moduleId)
        => _shell.CurrentScreen = new ModulePlaceholderViewModel(moduleId, this);
}
