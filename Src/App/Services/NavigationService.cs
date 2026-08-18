using HardwareAuditToolkit.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HardwareAuditToolkit.App.Services;

public sealed class NavigationService : INavigationService
{
    private readonly ShellViewModel _shell;
    private readonly IServiceProvider _services;

    public NavigationService(ShellViewModel shell, IServiceProvider services)
    {
        _shell = shell;
        _services = services;
    }

    public void NavigateToDashboard()
        => SetScreen(new DashboardViewModel(this));

    public void NavigateToModule(string moduleId)
    {
        object screen = moduleId switch
        {
            "system" => _services.GetRequiredService<SystemInfoModuleViewModel>(),
            "stress" => _services.GetRequiredService<CpuStressModuleViewModel>(),
            "keyboard" => _services.GetRequiredService<KeyboardTestModuleViewModel>(),
            "mouse" => _services.GetRequiredService<MouseTestModuleViewModel>(),
            "monitor" => _services.GetRequiredService<MonitorTestModuleViewModel>(),
            _ => ActivatorUtilities.CreateInstance<ModulePlaceholderViewModel>(_services, moduleId, this),
        };

        SetScreen(screen);
    }

    /// <summary>
    /// Swaps the current screen, disposing the outgoing view model (e.g. to
    /// unsubscribe from the event bus) so listeners don't leak across navigation
    /// (architecture §7 / Phase 7 resource audit).
    /// </summary>
    private void SetScreen(object screen)
    {
        if (_shell.CurrentScreen is IDisposable previous)
        {
            previous.Dispose();
        }

        _shell.CurrentScreen = screen;
    }
}
