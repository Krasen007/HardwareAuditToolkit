using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Core.Modules;
using HardwareAuditToolkit.Infrastructure;
using Xunit;

namespace HardwareAuditToolkit.Tests;

/// <summary>
/// Phase 7 fault-injection tests for <see cref="CpuStressModule"/>'s run-loop guard
/// (architecture §9.7 "degrade, don't crash"). The module's worker body is an internal
/// injection seam (default = the real <c>Burn</c> loop); here we substitute a body that
/// throws on a background worker and assert the run degrades to
/// <see cref="TestStatus.Failed"/> with a recorded finding — and the process is not
/// terminated. This is the "belt-and-suspenders" coverage called out as best verified
/// on a real thread.
/// </summary>
public class CpuStressFaultInjectionTests
{
    [Fact]
    public void WorkerThrow_ResolvesToFailed_AndEndsRunCleanly()
    {
        var module = new CpuStressModule(
            new FakeSensorProvider(),
            _ => throw new InvalidOperationException("simulated burn-in failure"),
            null);

        var completed = new ManualResetEventSlim(false);
        TestStatus? finalStatus = null;
        module.Start(status =>
        {
            finalStatus = status;
            completed.Set();
        });

        Assert.True(completed.Wait(TimeSpan.FromSeconds(15)), "worker failure completion never fired");
        Assert.Equal(TestStatus.Failed, finalStatus);
        Assert.False(module.IsRunning);
        Assert.Contains(module.Findings, f => f.StartsWith("Burn-in worker failed"));
    }

    private sealed class FakeSensorProvider : ISensorProvider
    {
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
        public IReadOnlyList<SensorReading> ReadAll() => [];
        public string? UnavailableReason => null;
    }
}