using System.Reflection;
using HardwareAuditToolkit.Core.Keyboard;
using HardwareAuditToolkit.Core.Modules;
using HardwareAuditToolkit.App.ViewModels;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareAuditToolkit.Tests;

/// <summary>
/// Validates the Phase 3 ANSI layout (OS-independent) and the
/// <see cref="KeyboardTestModule"/> behavior end-to-end through the orchestrator
/// using a fake raw-input source so no hardware is required.
/// </summary>
public class KeyboardModuleTests
{
    /// <summary>Fake capture source that lets a test inject key samples.</summary>
    private sealed class FakeRawKeyboardInput : IRawKeyboardInput
    {
        public event EventHandler<RawKeySample>? KeyReceived;
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }

        public void Start() => Started = true;
        public void Stop() => Stopped = true;
        public void Raise(RawKeySample sample) => KeyReceived?.Invoke(this, sample);
    }

    private static KeyboardTestModule BuildModule(out FakeRawKeyboardInput fake, out TestOrchestrator orchestrator)
    {
        fake = new FakeRawKeyboardInput();
        var module = new KeyboardTestModule(fake);
        var session = new AuditSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Hostname = Environment.MachineName,
            StartedAt = DateTime.UtcNow,
        };
        orchestrator = new TestOrchestrator(session, [ module ]);
        return module;
    }

    [Fact]
    public void Layout_HasExpectedKeys_NoDuplicateIds()
    {
        var layout = KeyboardLayout.Ansi;
        Assert.NotEmpty(layout);
        Assert.All(layout, k => Assert.True(k.Width > 0 && k.Height > 0));

        var duplicates = layout
            .GroupBy(k => k.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.Empty(duplicates);

        // Esc must be recognized as ordinary test data (architecture §6).
        Assert.Equal("Esc", KeyboardLayout.GetLabel(0x01));
        // A representative extended key (right Ctrl, E0 0x1D) must resolve uniquely.
        Assert.Equal("RCt", KeyboardLayout.GetLabel(0xE01D));
    }

    [Fact]
    public void Module_AllKeysPressedThenConfirm_Passed()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("keyboard", out _));
        Assert.True(module.IsRunning);

        foreach (var key in KeyboardLayout.Ansi)
        {
            fake.Raise(new RawKeySample { ScanCodeId = key.Id, IsKeyDown = true });
        }

        Assert.Equal(KeyboardLayout.Ansi.Count, module.PressedCount);
        module.Confirm();

        Assert.Empty(orchestrator.RunningModules);
        Assert.Equal(TestStatus.Passed, GetSessionResult(orchestrator).Status);
        Assert.True(fake.Stopped);
    }

    [Fact]
    public void Module_SomeKeysMissingThenConfirm_Warning()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("keyboard", out _));

        // Press only half the keys.
        var half = KeyboardLayout.Ansi.Take(KeyboardLayout.Ansi.Count / 2).ToList();
        foreach (var key in half)
        {
            fake.Raise(new RawKeySample { ScanCodeId = key.Id, IsKeyDown = true });
        }

        module.Confirm();
        Assert.Equal(TestStatus.Warning, GetSessionResult(orchestrator).Status);
        Assert.True(fake.Stopped);
    }

    [Fact]
    public void Module_FlagDefect_Failed()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("keyboard", out _));
        module.FlagDefect("Spacebar sticks.");
        Assert.Equal(TestStatus.Failed, GetSessionResult(orchestrator).Status);
        Assert.True(fake.Stopped);
    }

    [Fact]
    public void Module_Cancel_CancelledAndStopsCapture()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("keyboard", out _));
        orchestrator.CancelModule("keyboard");
        Assert.Equal(TestStatus.Cancelled, GetSessionResult(orchestrator).Status);
        Assert.True(fake.Stopped);
        Assert.False(module.IsRunning);
    }

    [Fact]
    public void Module_IgnoresKeysOutsideLayout()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("keyboard", out _));
        fake.Raise(new RawKeySample { ScanCodeId = 0xDEAD, IsKeyDown = true });
        fake.Raise(new RawKeySample { ScanCodeId = KeyboardLayout.Ansi[0].Id, IsKeyDown = false }); // key-up only
        Assert.Equal(0, module.PressedCount);
    }

    [Fact]
    public void App_ConfigureServices_RegistersKeyboardModule()
    {
        // Regression guard: navigation routes "keyboard" to the real view model and
        // the raw-input source + module are wired into DI. Mirrors the Phase 2
        // registration assertion.
        var services = new ServiceCollection();
        var configure = typeof(HardwareAuditToolkit.App.App).GetMethod(
            "ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(configure);
        configure!.Invoke(null, [ services ]);

        Assert.Single(services, d => d.ServiceType == typeof(KeyboardTestModuleViewModel)
                                     && d.Lifetime == ServiceLifetime.Transient);
        Assert.Single(services, d => d.ServiceType == typeof(IRawMouseInput));
        Assert.Single(services, d => d.ServiceType == typeof(MouseTestModuleViewModel)
                                     && d.Lifetime == ServiceLifetime.Transient);
        // keyboard, mouse, monitor, system, stress now register as ITestModule.
        Assert.Equal(5, services.Count(d => d.ServiceType == typeof(ITestModule)));
    }

    [Fact]
    public void Module_RepeatedKeyPress_TracksRepeatCount()
    {
        var module = BuildModule(out var fake, out var orchestrator);
        Assert.True(orchestrator.TryStartModule("keyboard", out _));

        int id = KeyboardLayout.Ansi[0].Id;
        fake.Raise(new RawKeySample { ScanCodeId = id, IsKeyDown = true });
        fake.Raise(new RawKeySample { ScanCodeId = id, IsKeyDown = true });
        fake.Raise(new RawKeySample { ScanCodeId = id, IsKeyDown = true });

        // Three presses of the same key: the repeat counter rises (drives the
        // per-key badge / log), but it is still only one distinct covered key.
        Assert.Equal(3, module.PressCountFor(id));
        Assert.Equal(1, module.PressedCount);
    }

    private static ModuleResult GetSessionResult(TestOrchestrator orchestrator)
    {
        // The orchestrator records into the session it was given; retrieve via the
        // running-modules list emptiness + the session's recorded results.
        var session = (AuditSession?)typeof(TestOrchestrator)
            .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(orchestrator);
        Assert.NotNull(session);
        var result = session!.Modules.Single(r => r.ModuleId == "keyboard");
        Assert.NotNull(result.CompletedAt);
        return result;
    }
}
