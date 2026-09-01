using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareAuditToolkit.Tests;

/// <summary>
/// App-layer tests for <see cref="NavigationService"/> routing (architecture §10 Phase 1):
/// the module-id → view-model map (the <see cref="ModuleScreenRegistry"/>, roadmap E1)
/// and the rejection of unknown module ids. A lightweight <see cref="IServiceProvider"/>
/// stands in for the DI container so the routing logic is exercised without
/// constructing the WPF view models or their dependencies.
/// </summary>
public class NavigationServiceTests
{
    [Theory]
    [InlineData("system", typeof(SystemInfoModuleViewModel))]
    [InlineData("stress", typeof(CpuStressModuleViewModel))]
    [InlineData("keyboard", typeof(KeyboardTestModuleViewModel))]
    [InlineData("mouse", typeof(MouseTestModuleViewModel))]
    [InlineData("monitor", typeof(MonitorTestModuleViewModel))]
    [InlineData(null, typeof(DashboardViewModel))]
    public void Navigate_RoutesToExpectedViewModel(string? id, Type expected)
    {
        var screen = NavigateOnce(id);

        Assert.IsType(expected, screen);
    }

    [Fact]
    public void NavigateToModule_UnknownId_Throws()
    {
        Assert.Throws<ArgumentException>(() => NavigateOnce("nonexistent widget"));
    }

    /// <summary>
    /// Builds a fresh shell + navigation service and performs exactly one navigation, so the
    /// outgoing screen is never disposed (the probe view models are uninitialized instances and
    /// must not have their <see cref="IDisposable.Dispose"/> invoked).
    /// </summary>
    private static object? NavigateOnce(string? id)
    {
        NavigationService? nav = null;
        var provider = new FuncServiceProvider(t =>
        {
            if (t == typeof(INavigationService))
            {
                return nav;
            }

            if (IsViewModelType(t))
            {
                return RuntimeHelpers.GetUninitializedObject(t);
            }

            return null;
        });

        // The same shape the composition root registers: one entry per module id.
        var registry = new ModuleScreenRegistry(
        [
            new KeyValuePair<string, Func<IServiceProvider, object>>("system", sp => RuntimeHelpers.GetUninitializedObject(typeof(SystemInfoModuleViewModel))),
            new KeyValuePair<string, Func<IServiceProvider, object>>("stress", sp => RuntimeHelpers.GetUninitializedObject(typeof(CpuStressModuleViewModel))),
            new KeyValuePair<string, Func<IServiceProvider, object>>("keyboard", sp => RuntimeHelpers.GetUninitializedObject(typeof(KeyboardTestModuleViewModel))),
            new KeyValuePair<string, Func<IServiceProvider, object>>("mouse", sp => RuntimeHelpers.GetUninitializedObject(typeof(MouseTestModuleViewModel))),
            new KeyValuePair<string, Func<IServiceProvider, object>>("monitor", sp => RuntimeHelpers.GetUninitializedObject(typeof(MonitorTestModuleViewModel))),
        ]);

        var shell = new ShellViewModel();
        nav = new NavigationService(shell, provider, registry);

        if (id is null)
        {
            nav.NavigateToDashboard();
        }
        else
        {
            nav.NavigateToModule(id);
        }

        return shell.CurrentScreen;
    }

    private static bool IsViewModelType(Type t)
        => t == typeof(DashboardViewModel)
           || t == typeof(SystemInfoModuleViewModel)
           || t == typeof(CpuStressModuleViewModel)
           || t == typeof(KeyboardTestModuleViewModel)
           || t == typeof(MouseTestModuleViewModel)
           || t == typeof(MonitorTestModuleViewModel);

    private sealed class FuncServiceProvider(Func<Type, object?> resolve) : IServiceProvider
    {
        public object? GetService(Type serviceType) => resolve(serviceType);
    }
}