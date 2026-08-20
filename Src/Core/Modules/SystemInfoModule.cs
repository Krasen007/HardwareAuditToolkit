using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;

namespace HardwareAuditToolkit.Core.Modules;

/// <summary>
/// <para>
/// Phase 2 — System Info module (architecture §10 Phase 2). Gathers a WMI/CIM
/// inventory via <see cref="SystemInfoProvider"/> and records it as structured
/// measurements + findings in the session. Non-exclusive: it may run alongside
/// an exclusive module such as the CPU stress test (architecture §4).
/// </para>
/// <para>
/// Inventory collection is informational, not a pass/fail test, so a successful
/// collection resolves as <see cref="TestStatus.Passed"/>; a total failure to
/// collect degrades to <see cref="TestStatus.Warning"/> rather than crashing.
/// </para>
/// </summary>
public sealed class SystemInfoModule(SystemInfoProvider provider) : ITestModule
{
    private readonly SystemInfoProvider _provider = provider;
    private readonly object _gate = new();
    private Action<TestStatus>? _onComplete;
    private int _runGeneration;

    public IModuleMetadata Metadata { get; } = new SystemInfoMetadata();

    public string ModuleId => "system";

    public ModulePhase CurrentPhase { get; private set; } = ModulePhase.NotStarted;

    public bool IsRunning => CurrentPhase is ModulePhase.Setup or ModulePhase.Running or ModulePhase.AwaitingOperatorConfirmation;

    public IList<ModuleMeasurement> Measurements { get; } = new List<ModuleMeasurement>();

    public IList<string> Findings { get; } = new List<string>();

    public IList<string> OperatorActions { get; } = new List<string>();

    public IList<string> Artifacts { get; } = new List<string>();

    public bool CheckPreconditions() => true;

    public void Start(Action<TestStatus> onComplete)
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            _onComplete = onComplete;
            CurrentPhase = ModulePhase.Setup;
            Measurements.Clear();
            Findings.Clear();
            OperatorActions.Clear();
            Artifacts.Clear();

            // Delegate to a worker thread so Start returns promptly (ITestModule
            // contract) and the UI thread is never blocked by WMI queries. The
            // generation guard ensures a worker from a cancelled/restarted run
            // never mutates state or fires the completion callback of a newer
            // run (ITestModule contract: onComplete fires exactly once per run).
            int generation = ++_runGeneration;
            Task.Run(() =>
            {
                try
                {
                    var snapshot = _provider.GetSnapshot();
                    Action<TestStatus>? cb;
                    lock (_gate)
                    {
                        if (generation != _runGeneration)
                        {
                            return; // superseded by Cancel or a newer Start
                        }

                        CurrentPhase = ModulePhase.Running;
                        Populate(snapshot);
                        CurrentPhase = ModulePhase.Complete;
                        cb = _onComplete;
                        _onComplete = null;
                    }

                    cb?.Invoke(TestStatus.Passed);
                }
                catch (Exception ex)
                {
                    Action<TestStatus>? cb;
                    lock (_gate)
                    {
                        if (generation != _runGeneration)
                        {
                            return;
                        }

                        Findings.Add($"System info collection failed: {ex.Message}");
                        CurrentPhase = ModulePhase.Complete;
                        cb = _onComplete;
                        _onComplete = null;
                    }

                    cb?.Invoke(TestStatus.Warning);
                }
            });
        }
    }

    public void Cancel()
    {
        Action<TestStatus>? cb;
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            CurrentPhase = ModulePhase.Cancelled;
            _runGeneration++; // invalidate any in-flight worker from this run
            cb = _onComplete;
            _onComplete = null;
        }

        // Invoke the completion callback outside the lock (it re-enters the
        // orchestrator, which may hold its own lock waiting on this gate).
        cb?.Invoke(TestStatus.Cancelled);
    }

    private void Populate(SystemInfoSnapshot s)
    {
        void Add(string label, string? value, string? context = null)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            Measurements.Add(new ModuleMeasurement
            {
                Timestamp = DateTime.UtcNow,
                Label = label,
                Value = value,
                Context = context,
            });
        }

        Add("Hostname", Environment.MachineName);
        Add("Operating system", s.OperatingSystem);
        Add("OS architecture", s.OsArchitecture);
        Add("CPU", s.CpuName);
        if (s.PhysicalCores is { } pc)
        {
            Add("Physical cores", pc.ToString(), "cpu");
        }

        if (s.LogicalProcessors is { } lp)
        {
            Add("Logical processors", lp.ToString(), "cpu");
        }

        Add("Total RAM", s.TotalRamFormatted);
        Add("Motherboard", s.Motherboard);
        Add("System", string.Join(" ", new[] { s.SystemManufacturer, s.SystemModel }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim());
        Add("BIOS", s.BiosVersion);
        foreach (var disk in s.Disks)
        {
            Add("Disk", disk, "storage");
        }

        Findings.Add($"Inventory captured for {Environment.MachineName}: {s.CpuName ?? "unknown CPU"}, " +
                     $"{s.TotalRamFormatted ?? "unknown RAM"}, {s.Disks.Count} fixed disk(s).");
    }

    private sealed class SystemInfoMetadata : IModuleMetadata
    {
        public string Id => "system";
        public string DisplayName => "System Info";
        public string Description => "WMI/CIM inventory: CPU, RAM, disk, BIOS.";
        public string Category => "system";
        public string[] RequiredCapabilities => [];
        public bool IsExclusive => false;
        public TimeSpan? MaxDuration => null;
    }
}
