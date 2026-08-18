using System.Collections.ObjectModel;
using HardwareAuditToolkit.App.Messages;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;
using CommunityToolkit.Mvvm.Messaging;

namespace HardwareAuditToolkit.App.Modules;

/// <summary>
/// <para>
/// Phase 4 — mouse test module (architecture §10 Phase 4, §5, §6). Exclusive and
/// raw-input driven: click/scroll/drag events are streamed from
/// <see cref="IRawMouseInput"/>, each button tracked through press → (optional)
/// drag → release. Raw capture is owned by the module, started in
/// <see cref="Start"/> and torn down in <see cref="Cancel"/> (and on completion)
/// so no raw-input registration leaks across navigation (Phase 7 cleanup, started
/// now).
/// </para>
/// <para>
/// Pass criteria = operator confirmation ("all buttons/scroll/tracing work")
/// becomes <see cref="TestStatus.Passed"/>; flagging a defect becomes
/// <see cref="TestStatus.Failed"/>. A drag that moves beyond the click threshold
/// is logged distinctly from a click, so an early release mid-drag is clearly
/// flagged. Unplugging the mouse mid-test is handled gracefully (§9.5): raw
/// input simply stops, and a disconnect while a button is held is recorded as an
/// incomplete drag/drop finding rather than freezing the module.
/// </para>
/// </summary>
public sealed class MouseTestModule : ITestModule
{
    // Movement (in raw-relative pixels) beyond which a held button is considered
    // to have started a drag rather than a click.
    private const double DragThreshold = 10.0;

    private readonly IRawMouseInput _raw;
    private readonly object _gate = new();
    private ModulePhase _phase = ModulePhase.NotStarted;
    private Action<TestStatus>? _onComplete;
    private EventHandler<RawMouseSample>? _handler;
    private bool _deviceSubscribed;

    private readonly Dictionary<ButtonId, DragState> _drags = new();
    private int _leftClicks;
    private int _rightClicks;
    private int _middleClicks;
    private int _wheelTicks;
    private int _dragCount;
    private int _lastMouseCount = -1;
    private bool _traceRecorded;

    public MouseTestModule(IRawMouseInput raw)
    {
        _raw = raw;
        Metadata = new MouseMetadata();
        foreach (ButtonId id in Enum.GetValues<ButtonId>())
        {
            _drags[id] = new DragState();
        }
    }

    public IModuleMetadata Metadata { get; }

    public string ModuleId => "mouse";

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

    public int LeftClicks { get { lock (_gate) return _leftClicks; } }
    public int RightClicks { get { lock (_gate) return _rightClicks; } }
    public int MiddleClicks { get { lock (_gate) return _middleClicks; } }
    public int WheelTicks { get { lock (_gate) return _wheelTicks; } }
    public int DragCount { get { lock (_gate) return _dragCount; } }

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
            ResetDragState();
            _traceRecorded = false;

            _handler = OnMouse;
            _raw.MouseReceived += _handler;
            _raw.Start();

            if (!_deviceSubscribed)
            {
                WeakReferenceMessenger.Default.Register<DeviceTopologyChangedMessage>(this, OnDeviceTopology);
                _deviceSubscribed = true;
            }

            CurrentPhase = ModulePhase.Running;
        }

        WeakReferenceMessenger.Default.Send(new MouseTestStatusMessage
        {
            Status = TestStatus.Running,
            Detail = "Click, scroll, and drag-hold each button. Use Ctrl+E or Exit Test to leave. Confirm when done.",
        });
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            StopInternal(TestStatus.Cancelled, "Mouse test cancelled.");
        }
    }

    /// <summary>
    /// Operator confirms the mouse works. Resolves <see cref="TestStatus.Passed"/>.
    /// </summary>
    public void Confirm()
    {
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            var summary = $"Operator confirmed. Clicks — L:{_leftClicks} R:{_rightClicks} M:{_middleClicks}; " +
                          $"wheel ticks:{_wheelTicks}; drags:{_dragCount}.";
            Findings.Add(summary);
            if (!_traceRecorded)
            {
                Findings.Add("Operator confirmed without running the tracing sub-screen.");
            }

            StopInternal(TestStatus.Passed, "Passed — operator confirmed all mouse functions work.");
        }
    }

    /// <summary>Operator flags a defective button/sensor; resolves <see cref="TestStatus.Failed"/>.</summary>
    public void FlagDefect(string? note = null)
    {
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            Findings.Add(note ?? "Operator flagged a defective mouse function.");
            StopInternal(TestStatus.Failed, "Failed — operator flagged a defect.");
        }
    }

    /// <summary>Resets click/scroll/drag counters. Only valid before starting a run.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            ResetDragState();
        }
    }

    /// <summary>Records the tracing sub-screen result as measurements + findings.</summary>
    public void RecordTrace(double coveragePercent, int coveredPoints, int targetPoints, string shape)
    {
        lock (_gate)
        {
            _traceRecorded = true;
            Measurements.Add(new ModuleMeasurement
            {
                Timestamp = DateTime.UtcNow,
                Label = "Tracing path coverage",
                Value = $"{coveragePercent:0.0} %",
                Context = "trace",
            });
            Findings.Add($"Tracing test ({shape}): {coveragePercent:0.0}% path coverage " +
                         $"({coveredPoints}/{targetPoints} target points hit).");
        }
    }

    private void OnMouse(object? _, RawMouseSample sample)
    {
        // Wheel event.
        if (sample.IsWheel)
        {
            int ticks = sample.WheelDelta;
            string dir = ticks > 0 ? "up" : "down";
            int abs = Math.Abs(ticks);
            int wheelTicks;
            string log;
            lock (_gate)
            {
                _wheelTicks++;
                wheelTicks = _wheelTicks;
                log = $"Wheel {dir} ({abs}).";
            }

            SendEvent(sample, log, wheelTicks: wheelTicks);
            return;
        }

        // Button transitions (possibly several in one sample).
        if (sample.IsButtonEvent)
        {
            HandleButton(ButtonId.Left, sample.Buttons.HasFlag(MouseButtonChanges.LeftDown), sample.Buttons.HasFlag(MouseButtonChanges.LeftUp), sample);
            HandleButton(ButtonId.Right, sample.Buttons.HasFlag(MouseButtonChanges.RightDown), sample.Buttons.HasFlag(MouseButtonChanges.RightUp), sample);
            HandleButton(ButtonId.Middle, sample.Buttons.HasFlag(MouseButtonChanges.MiddleDown), sample.Buttons.HasFlag(MouseButtonChanges.MiddleUp), sample);
        }

        // Movement while any button is held accumulates drag distance.
        if (sample.HasMovement)
        {
            lock (_gate)
            {
                double d = Math.Sqrt(sample.DeltaX * sample.DeltaX + sample.DeltaY * sample.DeltaY);
                foreach (var kvp in _drags)
                {
                    var state = kvp.Value;
                    if (state.Held)
                    {
                        state.Distance += d;
                        if (!state.DragStarted && state.Distance >= DragThreshold)
                        {
                            state.DragStarted = true;
                            _dragCount++;
                            string label = ButtonLabel(kvp.Key);
                            SendEvent(sample, $"{label} drag started ({state.Distance:0} px).", drags: _dragCount);
                        }
                    }
                }
            }
        }
    }

    private void HandleButton(ButtonId id, bool down, bool up, RawMouseSample sample)
    {
        if (!down && !up)
        {
            return;
        }

        string log;
        int left = 0, right = 0, middle = 0, drags = 0;
        lock (_gate)
        {
            var state = _drags[id];
            if (down)
            {
                state.Held = true;
                state.Distance = 0;
                state.DragStarted = false;
                state.DownAt = DateTime.UtcNow;
                log = $"{ButtonLabel(id)} button down.";
            }
            else
            {
                // Release.
                string result;
                if (state.DragStarted)
                {
                    double dist = state.Distance;
                    var ms = (DateTime.UtcNow - state.DownAt).TotalMilliseconds;
                    result = $"drag: {dist:0} px over {ms:0} ms (released mid-drag — drop detected)";
                }
                else
                {
                    result = "click";
                    switch (id)
                    {
                        case ButtonId.Left: _leftClicks++; break;
                        case ButtonId.Right: _rightClicks++; break;
                        case ButtonId.Middle: _middleClicks++; break;
                    }
                }

                state.Held = false;
                state.DragStarted = false;
                log = $"{ButtonLabel(id)} button up — {result}.";
            }

            left = _leftClicks;
            right = _rightClicks;
            middle = _middleClicks;
            drags = _dragCount;
        }

        SendEvent(sample, log, left, right, middle, drags);
    }

    private void OnDeviceTopology(object? _, DeviceTopologyChangedMessage message)
    {
        bool flag = false;
        string log = string.Empty;
        lock (_gate)
        {
            bool anyHeld = _drags.Values.Any(s => s.Held) || _drags.Values.Any(s => s.DragStarted);
            if (_lastMouseCount > 0 && message.MouseCount == 0 && IsRunning && anyHeld)
            {
                flag = true;
                log = "Mouse disconnected while a button was held — drag/drop incomplete (graceful).";
                Findings.Add(log);
            }

            _lastMouseCount = message.MouseCount;
        }

        if (flag)
        {
            // Keep the module running; just surface the note on the event bus.
            WeakReferenceMessenger.Default.Send(new MouseTestStatusMessage
            {
                Status = TestStatus.Running,
                Detail = log,
            });
        }
    }

    private void SendEvent(
        RawMouseSample sample,
        string log,
        int left = -1,
        int right = -1,
        int middle = -1,
        int drags = -1,
        int wheelTicks = -1)
    {
        WeakReferenceMessenger.Default.Send(new MouseEventMessage
        {
            Buttons = sample.Buttons,
            WheelDelta = sample.WheelDelta,
            DeltaX = sample.DeltaX,
            DeltaY = sample.DeltaY,
            IsButtonEvent = sample.IsButtonEvent,
            IsWheel = sample.IsWheel,
            HasMovement = sample.HasMovement,
            LogLine = log,
            LeftClicks = left < 0 ? LeftClicks : left,
            RightClicks = right < 0 ? RightClicks : right,
            MiddleClicks = middle < 0 ? MiddleClicks : middle,
            WheelTicks = wheelTicks < 0 ? WheelTicks : wheelTicks,
            Drags = drags < 0 ? DragCount : drags,
        });
    }

    private void StopInternal(TestStatus status, string detail)
    {
        if (_handler is not null)
        {
            _raw.MouseReceived -= _handler;
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

        if (_deviceSubscribed)
        {
            WeakReferenceMessenger.Default.Unregister<DeviceTopologyChangedMessage>(this);
            _deviceSubscribed = false;
        }

        CurrentPhase = status == TestStatus.Cancelled ? ModulePhase.Cancelled : ModulePhase.Complete;
        OperatorActions.Add(detail);

        WeakReferenceMessenger.Default.Send(new MouseTestStatusMessage
        {
            Status = status,
            Detail = detail,
        });

        var cb = _onComplete;
        _onComplete = null;
        cb?.Invoke(status);
    }

    private void ResetDragState()
    {
        _leftClicks = 0;
        _rightClicks = 0;
        _middleClicks = 0;
        _wheelTicks = 0;
        _dragCount = 0;
        _lastMouseCount = -1;
        foreach (var state in _drags.Values)
        {
            state.Held = false;
            state.Distance = 0;
            state.DragStarted = false;
            state.DownAt = default;
        }
    }

    private static string ButtonLabel(ButtonId id)
        => id switch
        {
            ButtonId.Left => "Left",
            ButtonId.Right => "Right",
            ButtonId.Middle => "Middle",
            _ => id.ToString(),
        };

    private enum ButtonId
    {
        Left,
        Right,
        Middle,
    }

    private sealed class DragState
    {
        public bool Held;
        public double Distance;
        public bool DragStarted;
        public DateTime DownAt;
    }

    private sealed class MouseMetadata : IModuleMetadata
    {
        public string Id => "mouse";
        public string DisplayName => "Mouse Test";
        public string Description => "Click/scroll/drag log and tracing accuracy.";
        public string Category => "mouse";
        public string[] RequiredCapabilities => new[] { "raw mouse input" };
        public bool IsExclusive => true;
        public TimeSpan? MaxDuration => TimeSpan.FromMinutes(30);
    }
}
