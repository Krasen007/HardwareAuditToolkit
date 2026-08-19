using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.App.Messages;
using HardwareAuditToolkit.App.Modules;
using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.Core;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// View model for the Phase 4 mouse test (architecture §10 Phase 4, §6). Drives
/// the test through the orchestrator, renders click/scroll/drag events from
/// <see cref="MouseEventMessage"/>, exposes Start/Confirm/Flag paths (each
/// independent of the global exit paths), and hosts the duck/bicycle tracing
/// sub-screen launched from within this module (not a separate exclusive module,
/// mirroring the keyboard WPM sub-screen).
/// </summary>
public sealed partial class MouseTestModuleViewModel : ObservableObject, IDisposable
{
    // Target canvas coordinate space (also the tracing tolerance basis).
    private const double TraceWidth = 600;
    private const double TraceHeight = 360;
    private const double TraceTolerance = 18.0;

    private readonly INavigationService _navigation;
    private readonly TestOrchestrator _orchestrator;
    private readonly MouseTestModule _module;
    private readonly Dispatcher _dispatcher;

    private readonly List<Point> _tracedPoints = new();
    private readonly List<Point> _targetPoints = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(FlagDefectCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(FlagDefectCommand))]
    private bool _isCompleted;

    [ObservableProperty]
    private string _statusDetail = "Press Start to begin capturing mouse input.";

    [ObservableProperty]
    private string _finalStatusText = string.Empty;

    [ObservableProperty]
    private int _leftClicks;

    [ObservableProperty]
    private int _rightClicks;

    [ObservableProperty]
    private int _middleClicks;

    [ObservableProperty]
    private int _wheelTicks;

    [ObservableProperty]
    private int _dragCount;

    [ObservableProperty]
    private bool _isTraceMode;

    [ObservableProperty]
    private string _traceTargetName = "duck";

    [ObservableProperty]
    private string _traceResultText = string.Empty;

    [ObservableProperty]
    private string _deviceWarning = string.Empty;

    /// <summary>True when a device-disconnect warning is active (drives banner visibility).</summary>
    [ObservableProperty]
    private bool _isDeviceWarning;

    public ObservableCollection<string> LogLines { get; }

    public PointCollection TraceTargetPoints { get; }

    public PointCollection TracePoints { get; }

    /// <summary>True while the operator is actively tracing (button held on the canvas).</summary>
    public bool IsTracing { get; private set; }

    public double TraceCanvasWidth => TraceWidth;
    public double TraceCanvasHeight => TraceHeight;

    public MouseTestModuleViewModel(
        INavigationService navigation,
        TestOrchestrator orchestrator,
        MouseTestModule module)
    {
        _navigation = navigation;
        _orchestrator = orchestrator;
        _module = module;
        _dispatcher = Application.Current.Dispatcher;

        LogLines = new ObservableCollection<string>();
        (TraceTargetPoints, _targetPoints) = BuildTraceTarget();
        TracePoints = new PointCollection();

        WeakReferenceMessenger.Default.Register<MouseEventMessage>(this, OnMouseEvent);
        WeakReferenceMessenger.Default.Register<MouseTestStatusMessage>(this, OnStatus);
        WeakReferenceMessenger.Default.Register<DeviceTopologyChangedMessage>(this, OnDeviceTopology);
    }

    private (PointCollection, List<Point>) BuildTraceTarget()
    {
        // A recognizable duck silhouette in the fixed tracing coordinate space.
        // Sampled as evenly spaced target points for coverage scoring.
        var waypoints = new (double X, double Y)[]
        {
            (60, 250), (60, 210), (120, 200), (180, 170), (240, 150),
            (280, 90), (330, 70), (360, 90), (350, 130), (380, 140),
            (345, 150), (320, 160), (300, 200), (280, 250), (220, 280),
            (150, 280), (80, 275), (60, 250),
        };

        var points = new List<Point>();
        var collection = new PointCollection();
        for (int i = 0; i < waypoints.Length - 1; i++)
        {
            var a = waypoints[i];
            var b = waypoints[i + 1];
            double segLen = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            int steps = Math.Max(1, (int)Math.Ceiling(segLen / 8.0));
            for (int s = 0; s < steps; s++)
            {
                double t = (double)s / steps;
                var p = new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                points.Add(p);
                collection.Add(p);
            }
        }

        return (collection, points);
    }

    private void OnMouseEvent(object? _, MouseEventMessage message)
    {
        _dispatcher.Invoke(() =>
        {
            LogLines.Add(message.LogLine);
            if (LogLines.Count > 500)
            {
                LogLines.RemoveAt(0);
            }

            LeftClicks = message.LeftClicks;
            RightClicks = message.RightClicks;
            MiddleClicks = message.MiddleClicks;
            WheelTicks = message.WheelTicks;
            DragCount = message.Drags;
        });
    }

    private void OnStatus(object? _, MouseTestStatusMessage message)
    {
        _dispatcher.Invoke(() =>
        {
            IsRunning = message.Status == TestStatus.Running;
            if (message.Status == TestStatus.Running)
            {
                IsCompleted = false;
                FinalStatusText = string.Empty;
                StatusDetail = message.Detail ?? StatusDetail;
            }
            else
            {
                IsCompleted = true;
                FinalStatusText = message.Status switch
                {
                    TestStatus.Passed => "Passed — operator confirmed all mouse functions work.",
                    TestStatus.Warning => "Warning — see findings.",
                    TestStatus.Failed => "Failed — operator flagged a defect.",
                    TestStatus.Cancelled => "Cancelled.",
                    _ => $"Ended ({message.Status}).",
                };
                StatusDetail = message.Detail ?? FinalStatusText;
            }
        });
    }

    private void OnDeviceTopology(object? _, DeviceTopologyChangedMessage message)
    {
        _dispatcher.Invoke(() =>
        {
            if (message.MouseCount == 0)
            {
                DeviceWarning = "No mouse detected. Connect a mouse to test; disconnects during a drag are recorded as incomplete.";
                IsDeviceWarning = true;
            }
            else
            {
                DeviceWarning = string.Empty;
                IsDeviceWarning = false;
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartTest()
    {
        LogLines.Clear();
        LeftClicks = RightClicks = MiddleClicks = WheelTicks = DragCount = 0;
        IsCompleted = false;
        FinalStatusText = string.Empty;
        DeviceWarning = string.Empty;
        IsDeviceWarning = false;
        StatusDetail = "Capturing… click, scroll, and drag each button.";

        if (_orchestrator.TryStartModule("mouse", out _))
        {
            IsRunning = true;
        }
    }

    private bool CanStart => !IsRunning && !IsCompleted;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
        => _module.Confirm();

    private bool CanConfirm => IsRunning;

    [RelayCommand(CanExecute = nameof(CanFlag))]
    private void FlagDefect()
        => _module.FlagDefect("Operator flagged a defective mouse function.");

    private bool CanFlag => IsRunning;

    [RelayCommand]
    private void Reset()
    {
        if (IsRunning)
        {
            return;
        }

        _module.Reset();
        LogLines.Clear();
        LeftClicks = RightClicks = MiddleClicks = WheelTicks = DragCount = 0;
        TracePoints.Clear();
        TraceResultText = string.Empty;
        IsCompleted = false;
        FinalStatusText = string.Empty;
        DeviceWarning = string.Empty;
        IsDeviceWarning = false;
        StatusDetail = "Reset. Press Start to begin capturing mouse input.";
    }

    [RelayCommand]
    private void ToggleTrace()
    {
        IsTraceMode = !IsTraceMode;
        TracePoints.Clear();
        TraceResultText = string.Empty;
        _tracedPoints.Clear();
    }

    // --- Tracing sub-screen (driven by the view's pointer events) -----------

    /// <summary>Begins a trace stroke (called on canvas mouse-down).</summary>
    public void StartTrace(double x, double y)
    {
        if (!IsTraceMode)
        {
            return;
        }

        IsTracing = true;
        _tracedPoints.Clear();
        TracePoints.Clear();
        AddTrace(x, y);
    }

    /// <summary>Appends a traced point (called on canvas mouse-move while held).</summary>
    public void AddTrace(double x, double y)
    {
        if (!IsTracing)
        {
            return;
        }

        _tracedPoints.Add(new Point(x, y));
        TracePoints.Add(new Point(x, y));
    }

    /// <summary>Ends a trace stroke and scores coverage (called on canvas mouse-up).</summary>
    public void EndTrace()
    {
        if (!IsTracing)
        {
            return;
        }

        IsTracing = false;

        int covered = 0;
        foreach (var target in _targetPoints)
        {
            foreach (var p in _tracedPoints)
            {
                double dx = p.X - target.X;
                double dy = p.Y - target.Y;
                if (dx * dx + dy * dy <= TraceTolerance * TraceTolerance)
                {
                    covered++;
                    break;
                }
            }
        }

        double coverage = _targetPoints.Count == 0 ? 0 : covered * 100.0 / _targetPoints.Count;
        TraceResultText = $"{TraceTargetName} traced: {coverage:0.0}% coverage ({covered}/{_targetPoints.Count}).";
        _module.RecordTrace(coverage, covered, _targetPoints.Count, TraceTargetName);
    }

    [RelayCommand]
    private void Back()
        => _navigation.NavigateToDashboard();

    public void Dispose()
    {
        WeakReferenceMessenger.Default.Unregister<MouseEventMessage>(this);
        WeakReferenceMessenger.Default.Unregister<MouseTestStatusMessage>(this);
        WeakReferenceMessenger.Default.Unregister<DeviceTopologyChangedMessage>(this);

        // Leaving the screen must stop capture so no raw-input registration leaks
        // (architecture Phase 7 cleanup, started in Phase 4).
        if (_orchestrator.RunningModules.Any(m => m.ModuleId == "mouse"))
        {
            _orchestrator.CancelModule("mouse");
        }
    }
}
