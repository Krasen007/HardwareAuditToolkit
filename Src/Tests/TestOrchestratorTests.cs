using HardwareAuditToolkit.Core;
using Xunit;

namespace HardwareAuditToolkit.Tests;

public class TestOrchestratorTests
{
    [Fact]
    public void StartModule_ThatCompletesImmediately_RecordsSingleResultInPlace()
    {
        var session = new AuditSession();
        var module = new FakeModule("sysinfo", isExclusive: false, autoComplete: TestStatus.Passed);
        using var orchestrator = new TestOrchestrator(session, [module]);

        bool started = orchestrator.TryStartModule("sysinfo", out string reason);

        Assert.True(started);
        Assert.Empty(reason);
        Assert.Single(session.Modules); // exactly one record per run, updated in place
        var result = session.Modules[0];
        Assert.Equal(TestStatus.Passed, result.Status);
        Assert.NotNull(result.CompletedAt);
        Assert.Equal(TestStatus.Passed, session.OverallStatus);
    }

    [Fact]
    public void StartModule_UnknownModule_ReturnsFalse()
    {
        using var orchestrator = new TestOrchestrator(new AuditSession(), []);

        Assert.False(orchestrator.TryStartModule("nope", out string reason));
        Assert.Contains("Unknown module", reason);
    }

    [Fact]
    public void StartModule_PreconditionsFail_ReturnsFalseAndAddsNoResult()
    {
        var session = new AuditSession();
        var module = new FakeModule("keyboard", isExclusive: true) { PreconditionsMet = false };
        using var orchestrator = new TestOrchestrator(session, [module]);

        Assert.False(orchestrator.TryStartModule("keyboard", out string reason));
        Assert.Contains("Preconditions", reason);
        Assert.Empty(session.Modules);
    }

    [Fact]
    public void StartModule_AlreadyRunning_ReturnsFalse()
    {
        var session = new AuditSession();
        var module = new FakeModule("keyboard", isExclusive: true);
        using var orchestrator = new TestOrchestrator(session, [module]);

        Assert.True(orchestrator.TryStartModule("keyboard", out _));
        Assert.False(orchestrator.TryStartModule("keyboard", out string reason));
        Assert.Contains("already running", reason);
        Assert.Single(session.Modules);
    }

    [Fact]
    public void ExclusiveModule_BlocksAnotherExclusiveModule()
    {
        var session = new AuditSession();
        var keyboard = new FakeModule("keyboard", isExclusive: true); // runs until cancelled
        var mouse = new FakeModule("mouse", isExclusive: true);
        using var orchestrator = new TestOrchestrator(session, [keyboard, mouse]);

        Assert.True(orchestrator.TryStartModule("keyboard", out _));
        Assert.False(orchestrator.TryStartModule("mouse", out string reason));
        Assert.Contains("one at a time", reason);
        Assert.Single(session.Modules);
        Assert.Same(keyboard, orchestrator.CurrentExclusiveModule);
    }

    [Fact]
    public void NonExclusiveModule_CanRunAlongsideExclusiveModule()
    {
        var session = new AuditSession();
        var keyboard = new FakeModule("keyboard", isExclusive: true);
        var sensors = new FakeModule("sensors", isExclusive: false, autoComplete: TestStatus.Passed);
        using var orchestrator = new TestOrchestrator(session, [keyboard, sensors]);

        Assert.True(orchestrator.TryStartModule("keyboard", out _));
        Assert.True(orchestrator.TryStartModule("sensors", out _));

        Assert.True(keyboard.IsRunning);
        Assert.Same(keyboard, orchestrator.CurrentExclusiveModule);
        Assert.Equal(TestStatus.Passed, session.Modules.Single(m => m.ModuleId == "sensors").Status);
    }

    [Fact]
    public void ExclusiveModule_CanStartWhileNonExclusiveModuleRuns()
    {
        var session = new AuditSession();
        var sensors = new FakeModule("sensors", isExclusive: false); // runs until cancelled
        var keyboard = new FakeModule("keyboard", isExclusive: true);
        using var orchestrator = new TestOrchestrator(session, [sensors, keyboard]);

        Assert.True(orchestrator.TryStartModule("sensors", out _));
        Assert.True(orchestrator.TryStartModule("keyboard", out _));
        Assert.Same(keyboard, orchestrator.CurrentExclusiveModule);
    }

    [Fact]
    public void CancelModule_RecordsCancelledStatus()
    {
        var session = new AuditSession();
        var keyboard = new FakeModule("keyboard", isExclusive: true);
        using var orchestrator = new TestOrchestrator(session, [keyboard]);

        Assert.True(orchestrator.TryStartModule("keyboard", out _));
        Assert.True(orchestrator.CancelModule("keyboard"));

        var result = session.Modules.Single();
        Assert.Equal(TestStatus.Cancelled, result.Status);
        Assert.NotNull(result.CompletedAt);
        Assert.Empty(orchestrator.RunningModules);
        Assert.Equal(TestStatus.Cancelled, session.OverallStatus);
    }

    [Fact]
    public void CancelModule_NotRunning_ReturnsFalse()
    {
        using var orchestrator = new TestOrchestrator(new AuditSession(), []);

        Assert.False(orchestrator.CancelModule("nope"));
    }

    [Fact]
    public void Timeout_ForceCancelsModule()
    {
        var session = new AuditSession();
        var keyboard = new FakeModule("keyboard", isExclusive: true, maxDuration: TimeSpan.FromMilliseconds(150));
        using var orchestrator = new TestOrchestrator(session, [keyboard]);

        Assert.True(orchestrator.TryStartModule("keyboard", out _));

        bool completed = SpinWait.SpinUntil(
            () => session.Modules.Single().CompletedAt.HasValue,
            TimeSpan.FromSeconds(10));

        Assert.True(completed, "module should have been force-cancelled by its timeout");
        var result = session.Modules.Single();
        Assert.Equal(TestStatus.Cancelled, result.Status);
        Assert.Contains(result.Findings, f => f.Contains("maximum duration"));
        Assert.Equal(TestStatus.Cancelled, session.OverallStatus);
    }

    [Fact]
    public void Timeout_ModuleCompletingInlineOnCancel_RecordsSingleResult()
    {
        // Real modules (keyboard/mouse/monitor) invoke the completion callback
        // synchronously from Cancel(). When such a module times out, the timeout
        // must not double-record the result (duplicate measurements/findings +
        // a spurious "maximum duration" finding appended to a run the module
        // already closed itself).
        var session = new AuditSession();
        var module = new FakeModule("keyboard", isExclusive: true, maxDuration: TimeSpan.FromMilliseconds(150))
        {
            CompleteOnCancel = true,
        };
        module.Measurements.Add(new ModuleMeasurement { Timestamp = DateTime.UtcNow, Label = "key", Value = "A" });
        using var orchestrator = new TestOrchestrator(session, [module]);

        Assert.True(orchestrator.TryStartModule("keyboard", out _));

        bool completed = SpinWait.SpinUntil(
            () => session.Modules.Single().CompletedAt.HasValue,
            TimeSpan.FromSeconds(10));

        Assert.True(completed, "module should have been force-cancelled by its timeout");
        var result = session.Modules.Single();
        Assert.Equal(TestStatus.Cancelled, result.Status);
        Assert.Single(result.Measurements); // copied exactly once, not twice
        Assert.DoesNotContain(result.Findings, f => f.Contains("maximum duration"));
    }

    [Fact]
    public void StartModule_StartThrows_ReturnsFalseAndRecordsFailed()
    {
        var session = new AuditSession();
        var module = new FakeModule("bad", isExclusive: false) { ThrowOnStart = true };
        using var orchestrator = new TestOrchestrator(session, [module]);

        Assert.False(orchestrator.TryStartModule("bad", out string reason));
        Assert.Contains("failed to start", reason);

        var result = session.Modules.Single();
        Assert.Equal(TestStatus.Failed, result.Status);
        Assert.NotNull(result.CompletedAt);
        Assert.Equal(TestStatus.Failed, session.OverallStatus);
    }

    [Fact]
    public void RestartAfterCompletion_CreatesSecondResultRecord()
    {
        var session = new AuditSession();
        var module = new FakeModule("sysinfo", isExclusive: false, autoComplete: TestStatus.Passed);
        using var orchestrator = new TestOrchestrator(session, [module]);

        Assert.True(orchestrator.TryStartModule("sysinfo", out _));
        Assert.True(orchestrator.TryStartModule("sysinfo", out _));
        Assert.Equal(2, session.Modules.Count);
    }

    private sealed class FakeModule(string id, bool isExclusive, TimeSpan? maxDuration = null, TestStatus? autoComplete = null) : ITestModule
    {
        private readonly TestStatus? _autoCompleteStatus = autoComplete;

        public string ModuleId { get; } = id;
        public IModuleMetadata Metadata { get; } = new FakeMetadata(id, isExclusive, maxDuration);
        public ModulePhase CurrentPhase { get; private set; } = ModulePhase.NotStarted;
        public bool IsRunning => CurrentPhase is ModulePhase.Setup or ModulePhase.Running or ModulePhase.AwaitingOperatorConfirmation;
        public IList<ModuleMeasurement> Measurements { get; } = [];
        public IList<string> Findings { get; } = [];
        public IList<string> OperatorActions { get; } = [];

        public bool PreconditionsMet { get; set; } = true;
        public bool ThrowOnStart { get; set; }

        public bool CompleteOnCancel { get; set; }

        public bool CheckPreconditions() => PreconditionsMet;

        public void Start(Action<TestStatus> onComplete)
        {
            _onComplete = onComplete;
            if (ThrowOnStart)
            {
                throw new InvalidOperationException("simulated start failure");
            }

            CurrentPhase = ModulePhase.Running;
            if (_autoCompleteStatus is { } status)
            {
                CurrentPhase = ModulePhase.Complete;
                onComplete(status);
            }
        }

        public void Cancel()
        {
            if (IsRunning)
            {
                CurrentPhase = ModulePhase.Cancelled;
                if (CompleteOnCancel)
                {
                    var cb = _onComplete;
                    _onComplete = null;
                    cb?.Invoke(TestStatus.Cancelled);
                }
            }
        }

        private Action<TestStatus>? _onComplete;

        private sealed class FakeMetadata(string id, bool isExclusive, TimeSpan? maxDuration) : IModuleMetadata
        {
            public string Id { get; } = id;
            public string DisplayName => Id;
            public string Description => $"Fake module '{Id}'.";
            public string Category => IsExclusive ? "exclusive" : "generic";
            public string[] RequiredCapabilities => [];
            public bool IsExclusive { get; } = isExclusive;
            public TimeSpan? MaxDuration { get; } = maxDuration;
        }
    }
}
