using System.Collections.Generic;
using System.Threading;
using HardwareAuditToolkit.Core;
using Xunit;

namespace HardwareAuditToolkit.Tests;

/// <summary>
/// Phase 7 crash-persistence wiring: the <see cref="TestOrchestrator"/> must call
/// <see cref="ISessionCheckpointStore.Save"/> after a module completes (architecture §7 / §9.7),
/// so a checkpoint is written without an explicit export. Uses a synchronous fake module and a
/// capturing fake store to assert the save fires on completion.
/// </summary>
public class OrchestratorCheckpointTests
{
    [Fact]
    public void ModuleCompletion_TriggersCheckpointSave()
    {
        var session = new AuditSession { SessionId = "chk", Hostname = "H", StartedAt = DateTime.UtcNow };
        var store = new FakeCheckpointStore();
        var module = new ImmediateModule("demo");

        var orchestrator = new TestOrchestrator(session, new List<ITestModule> { module }, checkpoint: store);

        Assert.True(orchestrator.TryStartModule("demo", out _));
        Assert.True(store.SaveCalled);
    }

    private sealed class ImmediateModule : ITestModule
    {
        public ImmediateModule(string id) => ModuleId = id;

        public IModuleMetadata Metadata { get; } = new ImmediateMetadata();
        public string ModuleId { get; }
        public ModulePhase CurrentPhase { get; private set; } = ModulePhase.NotStarted;
        public bool IsRunning => CurrentPhase is ModulePhase.Setup or ModulePhase.Running or ModulePhase.AwaitingOperatorConfirmation;
        public IList<ModuleMeasurement> Measurements { get; } = [];
        public IList<string> Findings { get; } = [];
        public IList<string> OperatorActions { get; } = [];

        public bool CheckPreconditions() => true;

        public void Start(Action<TestStatus> onComplete)
        {
            CurrentPhase = ModulePhase.Running;
            onComplete(TestStatus.Passed);
            CurrentPhase = ModulePhase.Complete;
        }

        public void Cancel()
        {
        }
    }

    private sealed class ImmediateMetadata : IModuleMetadata
    {
        public string Id => "demo";
        public string DisplayName => "Demo";
        public string Description => "demo";
        public string Category => "demo";
        public string[] RequiredCapabilities => [];
        public bool IsExclusive => false;
        public TimeSpan? MaxDuration => null;
    }

    private sealed class FakeCheckpointStore : ISessionCheckpointStore
    {
        public bool SaveCalled { get; private set; }

        public void Save(AuditSession session) => SaveCalled = true;
    }
}
