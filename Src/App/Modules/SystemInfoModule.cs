using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;

namespace HardwareAuditToolkit.App.Modules;

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
public sealed class SystemInfoModule : ITestModule
{
    private readonly SystemInfoProvider _provider;
    private readonly object _gate = new();
    private ModulePhase _phase = ModulePhase.NotStarted;
    private Action<TestStatus>? _onComplete;

    public SystemInfoModule(SystemInfoProvider provider)
    {
        _provider = provider;
        Metadata = new SystemInfoMetadata();
    }

    public IModuleMetadata Metadata { get; }

    public string ModuleId => "system";

    public ModulePhase CurrentPhase
    {
        get => _phase;
        private set => _phase = value;
    }

    public bool IsRunning => _phase is ModulePhase.Setup or ModulePhase.Running or ModulePhase.AwaitingOperatorConfirmation;

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
            // contract) and the UI thread is never blocked by WMI queries.
            Task.Run(() =>
            {
                try
                {
                    var snapshot = _provider.GetSnapshot();
                    CurrentPhase = ModulePhase.Running;
                    Populate(snapshot);
                    CurrentPhase = ModulePhase.Complete;
                    _onComplete?.Invoke(TestStatus.Passed);
                }
                catch (Exception ex)
                {
                    Findings.Add($"System info collection failed: {ex.Message}");
                    CurrentPhase = ModulePhase.Complete;
                    _onComplete?.Invoke(TestStatus.Warning);
                }
            });
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            CurrentPhase = ModulePhase.Cancelled;
            _onComplete?.Invoke(TestStatus.Cancelled);
        }
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
        public string[] RequiredCapabilities => Array.Empty<string>();
        public bool IsExclusive => false;
        public TimeSpan? MaxDuration => null;
    }
}
