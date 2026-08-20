using HardwareAuditToolkit.Core.Modules;
using HardwareAuditToolkit.App.ViewModels;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareAuditToolkit.Tests;

/// <summary>
/// Validates the Phase 5 <see cref="MonitorTestModule"/> behavior end-to-end through
/// the orchestrator using a fake <see cref="IDdcCiControl"/> so no hardware is
/// required. Confirms DDC/CI degrades gracefully to "unsupported" and that the
/// module can still Pass on operator confirmation alone (architecture §10 Phase 5).
/// </summary>
public class MonitorModuleTests
{
    /// <summary>Fake DDC/CI source that lets a test toggle support/injection.</summary>
    private sealed class FakeDdc : IDdcCiControl
    {
        public bool Supported = true;
        public int LastSetValue;

        public IReadOnlyList<MonitorInfo> EnumerateMonitors()
            => new[]
            {
                new MonitorInfo { Index = 0, FriendlyName = "Fake Display", Width = 1920, Height = 1080, IsPrimary = true },
                new MonitorInfo { Index = 1, FriendlyName = "Second Display", Width = 1280, Height = 1024, IsPrimary = false },
            };

        public BrightnessReading GetBrightness(int index)
            => Supported
                ? new BrightnessReading { Supported = true, Current = 50, Minimum = 0, Maximum = 100 }
                : new BrightnessReading { Supported = false, Detail = "DDC/CI not available." };

        public bool SetBrightness(int index, int value)
        {
            LastSetValue = value;
            return Supported;
        }
    }

    private static MonitorTestModule BuildModule(out FakeDdc fake, out TestOrchestrator orchestrator)
    {
        fake = new FakeDdc();
        var module = new MonitorTestModule(fake);
        var session = new AuditSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Hostname = Environment.MachineName,
            StartedAt = DateTime.UtcNow,
        };
        orchestrator = new TestOrchestrator(session, new ITestModule[] { module });
        return module;
    }

    [Fact]
    public void Module_Confirm_Passed()
    {
        var module = BuildModule(out _, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("monitor", out _));
        Assert.True(module.IsRunning);

        module.RecordPatternViewed("Solid — White");
        module.Confirm();

        Assert.Empty(orchestrator.RunningModules);
        Assert.Equal(TestStatus.Passed, GetSessionResult(orchestrator, "monitor").Status);
    }

    [Fact]
    public void Module_FlagDefect_Failed()
    {
        var module = BuildModule(out _, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("monitor", out _));
        module.FlagDefect("Dead pixel in corner.");
        Assert.Equal(TestStatus.Failed, GetSessionResult(orchestrator, "monitor").Status);
    }

    [Fact]
    public void Module_Cancel_Cancelled()
    {
        var module = BuildModule(out _, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("monitor", out _));
        orchestrator.CancelModule("monitor");
        Assert.Equal(TestStatus.Cancelled, GetSessionResult(orchestrator, "monitor").Status);
        Assert.False(module.IsRunning);
    }

    [Fact]
    public void Module_DdcUnsupported_StillRunsAndConfirms()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        fake.Supported = false;
        module.SetSelectedMonitor(0);
        Assert.False(module.DdcSupported);

        Assert.True(orchestrator.TryStartModule("monitor", out _));
        module.Confirm();
        Assert.Equal(TestStatus.Passed, GetSessionResult(orchestrator, "monitor").Status);
    }

    [Fact]
    public void Module_ApplyBrightness_UsesDdc()
    {
        var module = BuildModule(out var fake, out _);
        Assert.True(module.ApplyBrightness(73));
        Assert.Equal(73, fake.LastSetValue);
        Assert.Equal(73, module.BrightnessCurrent);
    }

    [Fact]
    public void App_ConfigureServices_RegistersMonitorModule()
    {
        var services = new ServiceCollection();
        var configure = typeof(HardwareAuditToolkit.App.App).GetMethod(
            "ConfigureServices", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(configure);
        configure!.Invoke(null, new object[] { services });

        Assert.Single(services, d => d.ServiceType == typeof(MonitorTestModuleViewModel)
                                     && d.Lifetime == ServiceLifetime.Transient);
        Assert.Single(services, d => d.ServiceType == typeof(IDdcCiControl));
        Assert.Single(services, d => d.ServiceType == typeof(MonitorTestModule));
        // keyboard, mouse, monitor, system, stress register as ITestModule.
        Assert.Equal(5, services.Count(d => d.ServiceType == typeof(ITestModule)));
    }

    private static ModuleResult GetSessionResult(TestOrchestrator orchestrator, string moduleId)
    {
        var session = (AuditSession?)typeof(TestOrchestrator)
            .GetField("_session", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(orchestrator);
        Assert.NotNull(session);
        var result = session!.Modules.Single(r => r.ModuleId == moduleId);
        Assert.NotNull(result.CompletedAt);
        return result;
    }
}
