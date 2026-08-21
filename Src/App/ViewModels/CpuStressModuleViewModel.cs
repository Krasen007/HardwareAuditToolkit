using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.Core.Messages;
using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.Core.Modules;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// View model for the Phase 2 CPU stress module (architecture §8, §10). Drives
/// the burn-in through the orchestrator, surfaces live telemetry from
/// <see cref="StressTelemetryMessage"/>, and exposes Start/Stop that are
/// independent of the global exit paths (§6). UI updates are marshaled to the
/// dispatcher because telemetry arrives on module/thread-pool threads. A live
/// line graph of CPU load % and maximum core temperature is also rendered from
/// the telemetry samples.
/// </summary>
public sealed partial class CpuStressModuleViewModel : ObservableObject, IDisposable
{
    private const double GraphW = 640;
    private const double GraphH = 220;
    private const int MaxSamples = 240; // ~4 min at the ambient 2s sensor cadence

    private readonly INavigationService _navigation;
    private readonly TestOrchestrator _orchestrator;
    private readonly CpuStressModule _stress;
    private readonly Dispatcher _dispatcher;
    private readonly List<double> _loadSamples = [];
    private readonly List<double> _tempSamples = [];

    public static double GraphWidth => GraphW;
    public static double GraphHeight => GraphH;

    /// <summary>Live CPU-load graph points (gold line).</summary>
    [ObservableProperty]
    private PointCollection _loadPoints = [];

    /// <summary>Live maximum-core-temperature graph points (blue line).</summary>
    [ObservableProperty]
    private PointCollection _tempPoints = [];

    [ObservableProperty]
    private int _coreCount = Environment.ProcessorCount;

    [ObservableProperty]
    private string _elapsedText = "0:00";

    [ObservableProperty]
    private string _targetText = "0:00";

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _cpuLoadText = "N/A";

    [ObservableProperty]
    private string _tempsText = "N/A (sensor unavailable)";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartTestCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTestCommand))]
    private bool _isCompleted;

    [ObservableProperty]
    private string _finalStatusText = string.Empty;

    [ObservableProperty]
    private string _statusDetail = "Press Start to begin the burn-in.";

    public CpuStressModuleViewModel(
        INavigationService navigation,
        TestOrchestrator orchestrator,
        CpuStressModule stress)
    {
        _navigation = navigation;
        _orchestrator = orchestrator;
        _stress = stress;
        _dispatcher = Application.Current.Dispatcher;
        WeakReferenceMessenger.Default.Register<StressTelemetryMessage>(this, OnTelemetry);
        // Ambient best-effort sensor broadcasts plot the current load while idle so
        // the graph is alive from the moment the screen opens — before Start is pressed.
        WeakReferenceMessenger.Default.Register<SensorReadingsMessage>(this, OnSensorReadings);

        TargetText = TimeSpan.FromSeconds(CpuStressModule.DefaultDurationSeconds).ToString(@"m\:ss");
    }

    private void OnTelemetry(object? _, StressTelemetryMessage message)
    {
        _dispatcher.Invoke(() =>
        {
            CoreCount = message.CoreCount > 0 ? message.CoreCount : CoreCount;
            ElapsedText = message.Elapsed.ToString(@"m\:ss");
            TargetText = message.TargetDuration.ToString(@"m\:ss");

            double pct = message.TargetDuration.TotalSeconds > 0
                ? Math.Min(100, message.Elapsed.TotalSeconds / message.TargetDuration.TotalSeconds * 100)
                : 0;
            ProgressPercent = (int)pct;

            CpuLoadText = message.CpuLoadPercent is { } load ? $"{load:0.0} %" : "N/A (sensor unavailable)";
            TempsText = message.CoreTempsCelsius.Count > 0
                ? string.Join(", ", message.CoreTempsCelsius.Select(t => t is { } v ? $"{v:0.0} °C" : "—"))
                : "N/A (sensor unavailable)";

            IsRunning = message.Running;
            if (!message.Running && message.FinalStatus is { } status)
            {
                IsCompleted = true;
                FinalStatusText = status switch
                {
                    TestStatus.Passed => "Completed — full duration reached.",
                    TestStatus.Cancelled => "Stopped by operator.",
                    _ => $"Ended ({status}).",
                };
                StatusDetail = FinalStatusText;
            }

            AppendSample(message.CpuLoadPercent, MaxCelsiusOf(message.CoreTempsCelsius));
        });
    }

    /// <summary>
    /// Ambient sensor snapshot while idle (no burn-in running). Plots the current
    /// system CPU load / temperature so the graph and readouts reflect live values
    /// from the moment the screen opens — the operator still has to press Start to
    /// actually begin the burn-in.
    /// </summary>
    private void OnSensorReadings(object? _, SensorReadingsMessage message)
    {
        // While a burn-in runs, StressTelemetryMessage drives the graph (it's the
        // authoritative source for the loaded run); ignore the ambient snapshot so we
        // don't double-plot.
        if (IsRunning)
        {
            return;
        }

        _dispatcher.Invoke(() =>
        {
            if (IsRunning)
            {
                return;
            }

            double? load = null;
            double tempMax = double.NaN;
            foreach (var r in message.Readings)
            {
                if (!IsCpuReading(r))
                {
                    continue;
                }

                if (r.SensorType == "Temperature")
                {
                    if (r.Value is { } v)
                    {
                        tempMax = double.IsNaN(tempMax) ? v : Math.Max(tempMax, (double)v);
                    }
                }
                else if (r.SensorType == "Load" &&
                         r.SensorName.Contains("Total", StringComparison.OrdinalIgnoreCase))
                {
                    load = r.Value;
                }
            }

            CpuLoadText = load is { } l ? $"{l:0.0} %" : "N/A (sensor unavailable)";
            TempsText = double.IsNaN(tempMax)
                ? "N/A (sensor unavailable)"
                : $"{tempMax:0.0} °C";

            AppendSample(load, tempMax);
        });
    }

    /// <summary>True when a reading belongs to the CPU (name/hardware match).</summary>
    private static bool IsCpuReading(SensorReading r)
        => r.SensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
           r.HardwareName.Contains("CPU", StringComparison.OrdinalIgnoreCase);

    /// <summary>Maximum non-null temperature in a list, or NaN when none present.</summary>
    private static double? MaxCelsiusOf(IReadOnlyList<float?> celsius)
    {
        double max = double.NaN;
        foreach (var t in celsius)
        {
            if (t is { } v)
            {
                max = double.IsNaN(max) ? v : Math.Max(max, (double)v);
            }
        }

        return double.IsNaN(max) ? null : max;
    }

    /// <summary>Records one sample for the graph and rebuilds both lines.</summary>
    private void AppendSample(double? load, double? tempMax)
    {
        _loadSamples.Add(load ?? double.NaN);
        _tempSamples.Add(tempMax ?? double.NaN);

        // Keep the graph a bounded trailing window so the x-axis step stays sane and
        // the surface never grows without bound during a long session.
        while (_loadSamples.Count > MaxSamples)
        {
            _loadSamples.RemoveAt(0);
            _tempSamples.RemoveAt(0);
        }

        LoadPoints = BuildSeries(_loadSamples, 0, 100);
        TempPoints = BuildSeries(_tempSamples);
    }

    /// <summary>
    /// Maps a series to a polyline in the fixed graph space. A fixed
    /// <paramref name="fixedMin"/>/<paramref name="fixedMax"/> axis is used when given
    /// (e.g. load 0–100 %); otherwise the series is auto-scaled to its min/max
    /// (temperature). NaN samples are skipped (no line through missing data).
    /// </summary>
    private static PointCollection BuildSeries(List<double> samples, double? fixedMin = null, double? fixedMax = null)
    {
        var clean = samples.Where(v => !double.IsNaN(v)).ToList();
        if (clean.Count == 0)
        {
            return [];
        }

        double min = fixedMin ?? clean.Min();
        double max = fixedMax ?? clean.Max();
        if (min == max)
        {
            max += 1; // avoid a flat/undefined scale for a constant series
        }

        double span = max - min;
        var pts = new List<Point>();
        double xStep = GraphW / (double)Math.Max(1, samples.Count - 1);
        for (int i = 0; i < samples.Count; i++)
        {
            var v = samples[i];
            if (double.IsNaN(v))
            {
                continue;
            }

            double t = (Math.Clamp(v, min, max) - min) / span;
            pts.Add(new Point(i * xStep, GraphH - (t * GraphH)));
        }

        return [.. pts];
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartTest()
    {
        _stress.Duration = TimeSpan.FromSeconds(CpuStressModule.DefaultDurationSeconds);
        IsCompleted = false;
        FinalStatusText = string.Empty;
        StatusDetail = "Burn-in running. Ctrl+E or Exit Test stops the app; Stop ends just this test.";
        LoadPoints = [];
        TempPoints = [];
        _loadSamples.Clear();
        _tempSamples.Clear();
        if (_orchestrator.TryStartModule("stress", out _))
        {
            IsRunning = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void StopTest()
        => _orchestrator.CancelModule("stress");

    private bool CanStart => !IsRunning;
    private bool CanStop => IsRunning;

    [RelayCommand]
    private void Back()
        => _navigation.NavigateToDashboard();

    public void Dispose()
    {
        WeakReferenceMessenger.Default.Unregister<StressTelemetryMessage>(this);
        WeakReferenceMessenger.Default.Unregister<SensorReadingsMessage>(this);
    }
}
