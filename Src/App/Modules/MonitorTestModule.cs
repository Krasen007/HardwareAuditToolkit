using System.Collections.ObjectModel;
using HardwareAuditToolkit.App.Messages;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;
using CommunityToolkit.Mvvm.Messaging;

namespace HardwareAuditToolkit.App.Modules;

/// <summary>
/// <para>
/// Phase 5 — monitor test module (architecture §10 Phase 5, §5, §6). Exclusive:
/// it owns the fullscreen pattern window rendered by the view. Two independent
/// checks make up the audit: (1) perceptual pattern inspection, where the
/// operator's confirmation becomes the recorded status, and (2) best-effort
/// DDC/CI brightness control via <see cref="IDdcCiControl"/>, which degrades to a
/// clean "unsupported" reading when the monitor or driver doesn't expose VCP 0x10
/// (architecture §10 Phase 5, DoD). A monitor can therefore still Pass on visual
/// confirmation alone.
/// </para>
/// <para>
/// DDC/CI probing runs on a worker task so <see cref="Start"/> returns promptly
/// (ITestModule contract); the result is published on the event bus as
/// <see cref="MonitorTestStatusMessage"/> and the cached support flags are
/// readable synchronously by the view model.
/// </para>
/// </summary>
public sealed class MonitorTestModule : ITestModule
{
    private readonly IDdcCiControl _ddc;
    private readonly object _gate = new();
    private ModulePhase _phase = ModulePhase.NotStarted;
    private Action<TestStatus>? _onComplete;
    private int _selectedIndex;
    private bool _ddcSupported;
    private int _brightnessMin;
    private int _brightnessMax;
    private int _brightnessCurrent;
    private string _ddcDetail = string.Empty;

    public MonitorTestModule(IDdcCiControl ddc)
    {
        _ddc = ddc;
        Metadata = new MonitorMetadata();
        try
        {
            Probe(0);
        }
        catch
        {
            // Construction must never throw; DDC probing is best-effort.
        }
    }

    public IModuleMetadata Metadata { get; }

    public string ModuleId => "monitor";

    public ModulePhase CurrentPhase
    {
        get { lock (_gate) return _phase; }
        private set { lock (_gate) _phase = value; }
    }

    public bool IsRunning => CurrentPhase is ModulePhase.Setup or ModulePhase.Running or ModulePhase.AwaitingOperatorConfirmation;

    public IList<ModuleMeasurement> Measurements { get; } = new List<ModuleMeasurement>();

    public IList<string> Findings { get; } = new List<string>();

    public IList<string> OperatorActions { get; } = new List<string>();

    public IList<string> Artifacts { get; } = new List<string>();

    /// <summary>Currently selected display index (DDC/CI target).</summary>
    public int SelectedMonitorIndex
    {
        get { lock (_gate) return _selectedIndex; }
    }

    public bool DdcSupported
    {
        get { lock (_gate) return _ddcSupported; }
    }

    public int BrightnessMin
    {
        get { lock (_gate) return _brightnessMin; }
    }

    public int BrightnessMax
    {
        get { lock (_gate) return _brightnessMax; }
    }

    public int BrightnessCurrent
    {
        get { lock (_gate) return _brightnessCurrent; }
    }

    public string DdcDetail
    {
        get { lock (_gate) return _ddcDetail; }
    }

    /// <summary>Current display topology; reflects live hot-plug/reconfiguration (§9.5).</summary>
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        try
        {
            return _ddc.EnumerateMonitors();
        }
        catch
        {
            return Array.Empty<MonitorInfo>();
        }
    }

    /// <summary>Selects the DDC/CI target display and re-probes its brightness support.</summary>
    public void SetSelectedMonitor(int index)
    {
        lock (_gate)
        {
            _selectedIndex = index;
        }

        Probe(index);
    }

    private void Probe(int index)
    {
        BrightnessReading reading;
        try
        {
            reading = _ddc.GetBrightness(index);
        }
        catch (Exception ex)
        {
            reading = new BrightnessReading { Supported = false, Detail = $"DDC/CI probe failed: {ex.Message}" };
        }

        lock (_gate)
        {
            _ddcSupported = reading.Supported;
            _brightnessMin = reading.Minimum;
            _brightnessMax = reading.Maximum;
            _brightnessCurrent = reading.Current;
            _ddcDetail = reading.Supported
                ? "DDC/CI brightness available."
                : (reading.Detail ?? "DDC/CI not available on this monitor.");
        }
    }

    /// <summary>Applies a brightness value via DDC/CI. Returns false when unsupported.</summary>
    public bool ApplyBrightness(int value)
    {
        int idx = SelectedMonitorIndex;
        bool ok;
        try
        {
            ok = _ddc.SetBrightness(idx, value);
        }
        catch
        {
            ok = false;
        }

        if (ok)
        {
            lock (_gate)
            {
                _brightnessCurrent = value;
            }
        }

        return ok;
    }

    /// <summary>Records that the operator viewed a given fullscreen pattern.</summary>
    public void RecordPatternViewed(string pattern)
    {
        lock (_gate)
        {
            Findings.Add($"Operator viewed pattern: {pattern}.");
            Measurements.Add(new ModuleMeasurement
            {
                Timestamp = DateTime.UtcNow,
                Label = "Pattern viewed",
                Value = pattern,
                Context = "pattern",
            });
        }
    }

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

            int idx = _selectedIndex;
            Task.Run(() =>
            {
                try
                {
                    var monitors = _ddc.EnumerateMonitors();
                    Probe(idx);
                    lock (_gate)
                    {
                        Measurements.Add(new ModuleMeasurement
                        {
                            Timestamp = DateTime.UtcNow,
                            Label = "Monitors detected",
                            Value = $"{monitors.Count}",
                            Context = "monitor",
                        });
                        foreach (var m in monitors)
                        {
                            Findings.Add($"Display {m.Index}: {m.FriendlyName} ({m.Width}x{m.Height}{(m.IsPrimary ? ", primary" : "")})");
                        }

                        Findings.Add(_ddcSupported
                            ? $"DDC/CI brightness supported on selected display: current {_brightnessCurrent} (range {_brightnessMin}–{_brightnessMax})."
                            : "DDC/CI brightness not available on the selected display (best-effort; pattern inspection still applies).");
                    }
                }
                catch (Exception ex)
                {
                    lock (_gate)
                    {
                        Findings.Add($"Monitor enumeration failed: {ex.Message}");
                    }
                }

                lock (_gate)
                {
                    // The operator may have confirmed or cancelled while we were
                    // probing; don't flip a terminal phase back to Running.
                    if (CurrentPhase != ModulePhase.Setup)
                    {
                        return;
                    }

                    CurrentPhase = ModulePhase.Running;
                }

                WeakReferenceMessenger.Default.Send(new MonitorTestStatusMessage
                {
                    Status = TestStatus.Running,
                    Detail = "Inspect patterns. Confirm when done, or flag a defect.",
                });
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

            cb = StopInternal(TestStatus.Cancelled, "Monitor test cancelled.");
        }

        cb?.Invoke(TestStatus.Cancelled);
    }

    /// <summary>Operator confirms the patterns render correctly → <see cref="TestStatus.Passed"/>.</summary>
    public void Confirm()
    {
        Action<TestStatus>? cb;
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            Findings.Add("Operator confirmed patterns render correctly on the selected display.");
            cb = StopInternal(TestStatus.Passed, "Passed — operator confirmed monitor patterns look correct.");
        }

        cb?.Invoke(TestStatus.Passed);
    }

    /// <summary>Operator flags a defect (dead pixel, uniformity, color) → <see cref="TestStatus.Failed"/>.</summary>
    public void FlagDefect(string? note = null)
    {
        Action<TestStatus>? cb;
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            Findings.Add(note ?? "Operator flagged a monitor defect (dead pixel, uniformity, or color).");
            cb = StopInternal(TestStatus.Failed, "Failed — operator flagged a monitor defect.");
        }

        cb?.Invoke(TestStatus.Failed);
    }

    /// <summary>Re-runs the DDC/CI probe. Only valid before starting a run.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            Probe(_selectedIndex);
        }
    }

    /// <summary>
    /// Records the result and publishes the status message. Caller must hold
    /// <see cref="_gate"/>. Returns the completion callback; the caller invokes
    /// it AFTER releasing the lock (the callback re-enters the orchestrator).
    /// </summary>
    private Action<TestStatus>? StopInternal(TestStatus status, string detail)
    {
        CurrentPhase = status == TestStatus.Cancelled ? ModulePhase.Cancelled : ModulePhase.Complete;
        OperatorActions.Add(detail);

        WeakReferenceMessenger.Default.Send(new MonitorTestStatusMessage
        {
            Status = status,
            Detail = detail,
        });

        var cb = _onComplete;
        _onComplete = null;
        return cb;
    }

    private sealed class MonitorMetadata : IModuleMetadata
    {
        public string Id => "monitor";
        public string DisplayName => "Monitor Test";
        public string Description => "Fullscreen patterns and DDC/CI brightness.";
        public string Category => "monitor";
        public string[] RequiredCapabilities => new[] { "DDC/CI" };
        public bool IsExclusive => true;
        public TimeSpan? MaxDuration => TimeSpan.FromMinutes(30);
    }
}
