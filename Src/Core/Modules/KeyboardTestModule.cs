using System.Collections.ObjectModel;
using HardwareAuditToolkit.Core.Keyboard;
using HardwareAuditToolkit.Core.Messages;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;
using CommunityToolkit.Mvvm.Messaging;

namespace HardwareAuditToolkit.Core.Modules;

/// <summary>
/// <para>
/// Phase 3 — keyboard test module (architecture §10 Phase 3, §5, §6). Exclusive
/// and scan-code driven: each physical key is tracked untested → pressed →
/// confirmed via the ANSI <see cref="KeyboardLayout"/>. Raw capture is owned by
/// <see cref="IRawKeyboardInput"/>, started in <see cref="Start"/> and torn down
/// in <see cref="Cancel"/> (and on completion) so no registration leaks across
/// navigation (architecture Phase 7 cleanup, started now).
/// </para>
/// <para>
/// Pass criteria = every expected key registered at least once; the operator's
/// confirmation ("all keys work") becomes the recorded status. <see cref="Esc"/>
/// is ordinary test data here (architecture §6) — it is captured like any key and
/// never triggers exit; <c>Ctrl+E</c> still exits via the independent hook (§9.2).
/// </para>
/// </summary>
public sealed class KeyboardTestModule : ITestModule
{
    private readonly IRawKeyboardInput _raw;
    private readonly object _gate = new();
    private ModulePhase _phase = ModulePhase.NotStarted;
    private Action<TestStatus>? _onComplete;
    private EventHandler<RawKeySample>? _handler;
    private readonly Dictionary<int, KeyState> _states = [];
    private readonly Dictionary<int, int> _pressCounts = [];
    private readonly IReadOnlyList<KeyLayoutDef> _layout;
    private int _pressedCount;

    public KeyboardTestModule(IRawKeyboardInput raw)
    {
        _raw = raw;
        _layout = KeyboardLayout.Ansi;
        Metadata = new KeyboardMetadata();
        ResetStates();
    }

    public IModuleMetadata Metadata { get; }

    public string ModuleId => "keyboard";

    public ModulePhase CurrentPhase
    {
        get { lock (_gate) return _phase; }
        private set { lock (_gate) _phase = value; }
    }

    public bool IsRunning => CurrentPhase is ModulePhase.Setup or ModulePhase.Running or ModulePhase.AwaitingOperatorConfirmation;

    public IList<ModuleMeasurement> Measurements { get; } = [];

    public IList<string> Findings { get; } = [];

    public IList<string> OperatorActions { get; } = [];

    public IList<string> Artifacts { get; } = [];

    /// <summary>Keys registered at least once this run.</summary>
    public int PressedCount
    {
        get { lock (_gate) return _pressedCount; }
    }

    /// <summary>Total expected keys (the coverage denominator).</summary>
    public int ExpectedCount => _layout.Count;

    /// <summary>
    /// How many times a specific key has been pressed this run (repeat counter).
    /// Exposed so the view model's per-key badge and the unit tests can observe
    /// repeated presses without tapping the event bus.
    /// </summary>
    public int PressCountFor(int id)
    {
        lock (_gate) return _pressCounts.TryGetValue(id, out var n) ? n : 0;
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
            ResetStates();

            _handler = OnKey;
            _raw.KeyReceived += _handler;
            _raw.Start();

            CurrentPhase = ModulePhase.Running;
        }

        WeakReferenceMessenger.Default.Send(new KeyboardTestStatusMessage
        {
            Status = TestStatus.Running,
            Detail = "Press each key once. Esc is captured as data — use Ctrl+E or Exit Test to leave. Confirm when done.",
        });
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

            cb = StopInternal(TestStatus.Cancelled, "Keyboard test cancelled.");
        }

        cb?.Invoke(TestStatus.Cancelled);
    }

    /// <summary>
    /// Operator confirms the keyboard works. Resolves <see cref="TestStatus.Passed"/>
    /// when every expected key registered, otherwise <see cref="TestStatus.Warning"/>
    /// with the missing keys listed.
    /// </summary>
    public void Confirm()
    {
        Action<TestStatus>? cb;
        TestStatus status;
        string detail;
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            var missing = _layout
                .Where(k => _states[k.Id] == KeyState.Untested)
                .Select(k => k.Label)
                .ToList();

            if (missing.Count == 0)
            {
                PromoteToConfirmed();
                Findings.Add("Operator confirmed: every expected key registered at least once.");
                status = TestStatus.Passed;
                detail = "Passed — all expected keys registered and operator confirmed.";
            }
            else
            {
                PromoteToConfirmed();
                Findings.Add($"Operator confirmed, but {missing.Count} key(s) were never pressed: {string.Join(", ", missing)}.");
                status = TestStatus.Warning;
                detail = "Warning — some keys were not pressed before confirmation.";
            }

            cb = StopInternal(status, detail);
        }

        cb?.Invoke(status);
    }

    /// <summary>Operator flags a defective key; resolves <see cref="TestStatus.Failed"/>.</summary>
    public void FlagDefect(string? note = null)
    {
        Action<TestStatus>? cb;
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            Findings.Add(note ?? "Operator flagged a defective key.");
            cb = StopInternal(TestStatus.Failed, "Failed — operator flagged a defect.");
        }

        cb?.Invoke(TestStatus.Failed);
    }

    /// <summary>Resets per-key coverage. Only valid before starting a run.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            ResetStates();
        }
    }

    /// <summary>Records the WPM/accuracy sub-screen result as measurements + findings.</summary>
    public void RecordWpm(double grossWpm, double accuracyPercent, string sample)
    {
        lock (_gate)
        {
            Measurements.Add(new ModuleMeasurement
            {
                Timestamp = DateTime.UtcNow,
                Label = "Typing gross WPM",
                Value = $"{grossWpm:0.0}",
                Context = "wpm",
            });
            Measurements.Add(new ModuleMeasurement
            {
                Timestamp = DateTime.UtcNow,
                Label = "Typing accuracy",
                Value = $"{accuracyPercent:0.0} %",
                Context = "wpm",
            });
            Findings.Add($"Typing test: {grossWpm:0.0} gross WPM, {accuracyPercent:0.0}% accuracy over {sample.Length} chars.");
        }
    }

    private void OnKey(object? _, RawKeySample sample)
    {
        if (!sample.IsKeyDown)
        {
            return; // only track presses (make codes); releases don't change coverage
        }

        KeyState newState;
        int pressed;
        int pressCount;
        string logLine;
        lock (_gate)
        {
            if (!_states.TryGetValue(sample.ScanCodeId, out var prior))
            {
                return; // not in the ANSI layout — ignore exotic/extra keys
            }

            // Count every press (including repeats of an already-pressed key) so
            // the view can show a repeat badge instead of a single green fill.
            pressCount = (_pressCounts.TryGetValue(sample.ScanCodeId, out var seen) ? seen : 0) + 1;
            _pressCounts[sample.ScanCodeId] = pressCount;

            if (prior == KeyState.Untested)
            {
                _states[sample.ScanCodeId] = KeyState.Pressed;
                _pressedCount++;
            }

            newState = _states[sample.ScanCodeId];
            pressed = _pressedCount;
            logLine = $"{KeyboardLayout.GetLabel(sample.ScanCodeId) ?? $"scan-{sample.ScanCodeId}"} — press #{pressCount}";

            if (pressed == _layout.Count && CurrentPhase == ModulePhase.Running)
            {
                CurrentPhase = ModulePhase.AwaitingOperatorConfirmation;
            }
        }

        WeakReferenceMessenger.Default.Send(new KeyEventMessage
        {
            KeyId = sample.ScanCodeId,
            Label = KeyboardLayout.GetLabel(sample.ScanCodeId) ?? sample.ScanCodeId.ToString(),
            IsKeyDown = true,
            NewState = newState,
            PressedCount = pressed,
            ExpectedCount = _layout.Count,
            PressCount = pressCount,
            LogLine = logLine,
        });

        if (pressed == _layout.Count)
        {
            WeakReferenceMessenger.Default.Send(new KeyboardTestStatusMessage
            {
                Status = TestStatus.Running,
                Detail = "All expected keys registered. Confirm if the keyboard works, or flag a defect.",
            });
        }
    }

    private void PromoteToConfirmed()
    {
        foreach (var key in _layout)
        {
            if (_states.TryGetValue(key.Id, out var s) && s == KeyState.Pressed)
            {
                _states[key.Id] = KeyState.Confirmed;
            }
        }
    }

    /// <summary>
    /// Tears down raw capture, records the result, and publishes the status.
    /// Caller must hold <see cref="_gate"/>. Returns the completion callback;
    /// the caller invokes it AFTER releasing the lock (it re-enters the
    /// orchestrator, which may hold its own lock waiting on this gate).
    /// </summary>
    private Action<TestStatus>? StopInternal(TestStatus status, string detail)
    {
        if (_handler is not null)
        {
            _raw.KeyReceived -= _handler;
            _handler = null;
        }

        try
        {
            _raw.Stop();
        }
        catch
        {
            // Best-effort teardown; never block completion on a capture failure.
        }

        CurrentPhase = status == TestStatus.Cancelled ? ModulePhase.Cancelled : ModulePhase.Complete;
        OperatorActions.Add(detail);

        WeakReferenceMessenger.Default.Send(new KeyboardTestStatusMessage
        {
            Status = status,
            Detail = detail,
        });

        var cb = _onComplete;
        _onComplete = null;
        return cb;
    }

    private void ResetStates()
    {
        _states.Clear();
        _pressCounts.Clear();
        foreach (var key in _layout)
        {
            _states[key.Id] = KeyState.Untested;
        }

        _pressedCount = 0;
    }

    private sealed class KeyboardMetadata : IModuleMetadata
    {
        public string Id => "keyboard";
        public string DisplayName => "Keyboard Test";
        public string Description => "Per-key coverage, WPM and accuracy.";
        public string Category => "keyboard";
        public string[] RequiredCapabilities => [ "raw keyboard input" ];
        public bool IsExclusive => true;
        // A long, unattended-friendly budget; the operator confirms well before this.
        public TimeSpan? MaxDuration => TimeSpan.FromMinutes(30);
    }
}
