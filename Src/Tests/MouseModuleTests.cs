using HardwareAuditToolkit.Core.Modules;
using HardwareAuditToolkit.App.ViewModels;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareAuditToolkit.Tests;

/// <summary>
/// Validates the Phase 4 <see cref="MouseTestModule"/> behavior end-to-end through
/// the orchestrator using a fake raw-input source so no hardware is required.
/// </summary>
public class MouseModuleTests
{
    /// <summary>Fake capture source that lets a test inject mouse samples.</summary>
    private sealed class FakeRawMouseInput : IRawMouseInput
    {
        public event EventHandler<RawMouseSample>? MouseReceived;
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }

        public void Start() => Started = true;
        public void Stop() => Stopped = true;
        public void Raise(RawMouseSample sample) => MouseReceived?.Invoke(this, sample);
    }

    private static MouseTestModule BuildModule(out FakeRawMouseInput fake, out TestOrchestrator orchestrator)
    {
        fake = new FakeRawMouseInput();
        var module = new MouseTestModule(fake);
        var session = new AuditSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Hostname = Environment.MachineName,
            StartedAt = DateTime.UtcNow,
        };
        orchestrator = new TestOrchestrator(session, [module]);
        return module;
    }

    [Fact]
    public void Module_AllButtonsScrollThenConfirm_Passed()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("mouse", out _));
        Assert.True(module.IsRunning);

        // Left click, right click, middle click, a wheel tick, then a left drag.
        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.LeftDown });
        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.LeftUp });
        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.RightDown });
        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.RightUp });
        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.MiddleDown });
        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.MiddleUp });
        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.Wheel, WheelDelta = 120 });

        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.LeftDown });
        for (int i = 0; i < 10; i++)
        {
            fake.Raise(new RawMouseSample { DeltaX = 10, DeltaY = 5 });
        }

        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.LeftUp });

        Assert.Equal(1, module.LeftClicks);
        Assert.Equal(1, module.RightClicks);
        Assert.Equal(1, module.MiddleClicks);
        Assert.Equal(1, module.WheelTicks);
        Assert.Equal(1, module.DragCount);

        module.Confirm();
        Assert.Empty(orchestrator.RunningModules);
        Assert.Equal(TestStatus.Passed, GetSessionResult(orchestrator).Status);
        Assert.True(fake.Stopped);
    }

    [Fact]
    public void Module_DragDetectedAndFlaggedInLog()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("mouse", out _));

        // Hold left, move well past the click threshold, release.
        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.LeftDown });
        for (int i = 0; i < 20; i++)
        {
            fake.Raise(new RawMouseSample { DeltaX = 15, DeltaY = 0 });
        }

        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.LeftUp });

        Assert.Equal(1, module.DragCount);
        Assert.Equal(0, module.LeftClicks); // moved → drag, not a click

        orchestrator.CancelModule("mouse");
        Assert.True(fake.Stopped);
    }

    [Fact]
    public void Module_ReleaseInSameSampleAsFinalMovement_StillADrag()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("mouse", out _));

        // Hold left and move past the threshold, then release in the SAME raw
        // sample that carries the last movement — the movement must be counted
        // before the release is classified, or this would be reported as a click.
        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.LeftDown });
        for (int i = 0; i < 20; i++)
        {
            fake.Raise(new RawMouseSample { DeltaX = 15, DeltaY = 0 });
        }

        fake.Raise(new RawMouseSample
        {
            Buttons = MouseButtonChanges.LeftUp,
            DeltaX = 20,
            DeltaY = 0,
        });

        Assert.Equal(1, module.DragCount);
        Assert.Equal(0, module.LeftClicks);

        orchestrator.CancelModule("mouse");
        Assert.True(fake.Stopped);
    }

    [Fact]
    public void Module_ReleaseInSameSampleAsMovementBelowThreshold_StillAClick()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("mouse", out _));

        // A click whose release sample carries a tiny movement stays a click.
        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.LeftDown });
        fake.Raise(new RawMouseSample { Buttons = MouseButtonChanges.LeftUp, DeltaX = 3, DeltaY = 2 });

        Assert.Equal(0, module.DragCount);
        Assert.Equal(1, module.LeftClicks);

        orchestrator.CancelModule("mouse");
        Assert.True(fake.Stopped);
    }

    [Fact]
    public void Module_FlagDefect_Failed()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("mouse", out _));
        module.FlagDefect("Right button sticks.");
        Assert.Equal(TestStatus.Failed, GetSessionResult(orchestrator).Status);
        Assert.True(fake.Stopped);
    }

    [Fact]
    public void Module_Cancel_CancelledAndStopsCapture()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("mouse", out _));
        orchestrator.CancelModule("mouse");
        Assert.Equal(TestStatus.Cancelled, GetSessionResult(orchestrator).Status);
        Assert.True(fake.Stopped);
        Assert.False(module.IsRunning);
    }

    [Fact]
    public void Module_RecordTrace_AddsMeasurement()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("mouse", out _));
        module.RecordTrace(87.5, 70, 80, "duck");
        module.Confirm();
        var result = GetSessionResult(orchestrator);
        Assert.Contains(result.Measurements, m => m.Label == "Tracing path coverage" && (m.Value ?? string.Empty).Contains("87") && (m.Value ?? string.Empty).Contains('%'));
        Assert.Contains(result.Findings, f => (f ?? string.Empty).Contains("path coverage") && (f ?? string.Empty).Contains("87"));
    }

    [Fact]
    public void App_ConfigureServices_RegistersMouseModule()
    {
        var services = new ServiceCollection();
        var configure = typeof(HardwareAuditToolkit.App.App).GetMethod(
            "ConfigureServices", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(configure);
        configure!.Invoke(null, [services]);

        Assert.Single(services, d => d.ServiceType == typeof(MouseTestModuleViewModel)
                                     && d.Lifetime == ServiceLifetime.Transient);
        Assert.Single(services, d => d.ServiceType == typeof(IRawMouseInput));
        Assert.Equal(5, services.Count(d => d.ServiceType == typeof(ITestModule)));
    }

    private static ModuleResult GetSessionResult(TestOrchestrator orchestrator)
    {
        var session = (AuditSession?)typeof(TestOrchestrator)
            .GetField("_session", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(orchestrator);
        Assert.NotNull(session);
        var result = session!.Modules.Single(r => r.ModuleId == "mouse");
        Assert.NotNull(result.CompletedAt);
        return result;
    }
}
