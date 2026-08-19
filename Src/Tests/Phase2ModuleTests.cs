using System.Reflection;
using HardwareAuditToolkit.App.Modules;
using HardwareAuditToolkit.App.ViewModels;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareAuditToolkit.Tests;

/// <summary>
/// Validates the Phase 2 wiring: the providers and modules compose through DI,
/// the orchestrator discovers them, and both modules run to a terminal state
/// without throwing. Hardware access is best-effort, so this also confirms the
/// providers degrade gracefully rather than crash the run.
/// </summary>
public class Phase2ModuleTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SystemInfoProvider>();
        services.AddSingleton<ISensorProvider, LibreHardwareMonitorSensorProvider>();
        services.AddSingleton<SystemInfoModule>();
        services.AddSingleton<CpuStressModule>();
        services.AddSingleton<ITestModule>(sp => sp.GetRequiredService<SystemInfoModule>());
        services.AddSingleton<ITestModule>(sp => sp.GetRequiredService<CpuStressModule>());

        var session = new AuditSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Hostname = Environment.MachineName,
            StartedAt = DateTime.UtcNow,
        };
        services.AddSingleton(session);
        services.AddSingleton<TestOrchestrator>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Composition_DiscoversPhase2Modules()
    {
        using var provider = Build();
        var orchestrator = provider.GetRequiredService<TestOrchestrator>();

        Assert.Contains(orchestrator.Modules, m => m.ModuleId == "system");
        Assert.Contains(orchestrator.Modules, m => m.ModuleId == "stress");
    }

    [Fact]
    public void SystemInfoModule_RunsToTerminalState()
    {
        using var provider = Build();
        var orchestrator = provider.GetRequiredService<TestOrchestrator>();

        Assert.True(orchestrator.TryStartModule("system", out _));
        bool done = SpinWait.SpinUntil(
            () => orchestrator.RunningModules.All(m => m.ModuleId != "system") &&
                  provider.GetRequiredService<AuditSession>().Modules.Any(r => r.ModuleId == "system" && r.CompletedAt.HasValue),
            TimeSpan.FromSeconds(15));

        Assert.True(done, "system info module should complete");
        var result = provider.GetRequiredService<AuditSession>().Modules.Single(r => r.ModuleId == "system");
        Assert.True(result.Status is TestStatus.Passed or TestStatus.Warning,
            $"unexpected status {result.Status}");
    }

    [Fact]
    public void CpuStressModule_StartsAndCancels()
    {
        using var provider = Build();
        var orchestrator = provider.GetRequiredService<TestOrchestrator>();

        Assert.True(orchestrator.TryStartModule("stress", out _));
        // Give it a moment to spin up worker threads.
        Thread.Sleep(500);
        Assert.Single(orchestrator.RunningModules);

        Assert.True(orchestrator.CancelModule("stress"));
        bool done = SpinWait.SpinUntil(
            () => provider.GetRequiredService<AuditSession>().Modules.Any(r => r.ModuleId == "stress" && r.CompletedAt.HasValue),
            TimeSpan.FromSeconds(15));

        Assert.True(done, "stress module should stop on cancel");
        var result = provider.GetRequiredService<AuditSession>().Modules.Single(r => r.ModuleId == "stress");
        Assert.Equal(TestStatus.Cancelled, result.Status);
    }

    [Fact]
    public void SensorProvider_OpensWithoutThrowing()
    {
        using var provider = new LibreHardwareMonitorSensorProvider();
        // No assertions on readings: best-effort. Just confirm Open/Start/Stop
        // never throw (architecture §7 degradation rule).
        var ex = Record.Exception(() =>
        {
            provider.Start();
            Thread.Sleep(200);
            _ = provider.ReadAll();
            provider.Stop();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void App_ConfigureServices_RegistersModuleViewModels()
    {
        // Regression guard for the Phase 2 wiring. NavigationService navigates to the
        // System Info and CPU stress screens via GetRequiredService<SystemInfoModuleViewModel>
        // / GetRequiredService<CpuStressModuleViewModel>. If those view models are not
        // registered, navigation throws InvalidOperationException and no hardware info is
        // shown. Assert the descriptors exist (and are transient, since they are disposed on
        // navigation-away) so a future cleanup cannot silently drop the registration.
        var services = new ServiceCollection();
        var configure = typeof(HardwareAuditToolkit.App.App).GetMethod(
            "ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(configure);
        configure!.Invoke(null, [services]);

        var infoDescriptor = Assert.Single(
            services, d => d.ServiceType == typeof(SystemInfoModuleViewModel));
        var stressDescriptor = Assert.Single(
            services, d => d.ServiceType == typeof(CpuStressModuleViewModel));

        Assert.Equal(ServiceLifetime.Transient, infoDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Transient, stressDescriptor.Lifetime);
    }
}
