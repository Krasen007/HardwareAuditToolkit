using HardwareAuditToolkit.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HardwareAuditToolkit.App.Services;

/// <summary>
/// View-model-first navigation over the <see cref="ModuleScreenRegistry"/> (roadmap
/// E1): module ids route through one table built in the DI composition root instead
/// of a hardcoded switch.
/// </summary>
public sealed class NavigationService(ShellViewModel shell, IServiceProvider services, ModuleScreenRegistry screens) : INavigationService
{
    private readonly ShellViewModel _shell = shell;
    private readonly IServiceProvider _services = services;
    private readonly ModuleScreenRegistry _screens = screens;

    public void NavigateToDashboard()
        => SetScreen(_services.GetRequiredService<DashboardViewModel>());

    public void NavigateToModule(string moduleId)
        => SetScreen(_screens.Resolve(moduleId, _services));

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
        _shell.IsDashboard = screen is DashboardViewModel;
    }
}
