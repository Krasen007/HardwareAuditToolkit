using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;
using HardwareAuditToolkit.Core.Messages;

namespace HardwareAuditToolkit.Core.Modules;

/// <summary>
/// <para>
/// Phase 2 — CPU stress / burn-in module (architecture §8). Loads every logical
/// core with one worker thread per core, all at <see cref="ThreadPriority.BelowNormal"/>
/// so the OS still favors the UI and the Ctrl+E hook thread under contention.
/// </para>
/// <para>
/// There is no automatic thermal cutoff in v1 (temperature access is best-effort
/// and cannot be relied upon), so the run is bounded by a fixed duration cap
/// (<see cref="Duration"/>, default 5 minutes) with a prominent manual Stop and
/// the global exit paths (§6) always available. Telemetry — elapsed time, total
/// CPU load, and any available core temperatures — is published on the event bus
/// as <see cref="Messages.StressTelemetryMessage"/> so the view stays live.
/// </para>
/// <para>
/// Completing the full duration resolves as <see cref="TestStatus.Passed"/>; a
/// deliberate operator stop via <see cref="CompleteEarly"/> also resolves as
/// <see cref="TestStatus.Passed"/> with the achieved duration recorded as a
/// finding (a planned 30-second smoke test is a pass, not a cancellation).
/// Only a genuine abort (Ctrl+E / Exit Test) records
/// <see cref="TestStatus.Cancelled"/> (architecture §4).
/// </para>
/// </summary>
public sealed class CpuStressModule : ITestModule
{
    public const int DefaultDurationSeconds = 300; // §8 conservative fixed cap.

    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadExecutionState(uint esFlags);

    private readonly ISensorProvider _sensors;
    private readonly Action<CancellationToken> _workerBody;
    private readonly IDiagnosticLog? _log;
    private readonly object _gate = new();
    private Action<TestStatus>? _onComplete;
    private Thread[] _workers = [];
    private CancellationTokenSource? _cts;
    private Timer? _telemetryTimer;
    private DateTime _startedAt;
    private int _coreCount;
    private TimeSpan _duration = TimeSpan.FromSeconds(DefaultDurationSeconds);

    public CpuStressModule(ISensorProvider sensors) : this(sensors, null, null)
    {
    }

    /// <summary>Production ctor: real burn loop with an optional diagnostics sink.</summary>
    public CpuStressModule(ISensorProvider sensors, IDiagnosticLog? log) : this(sensors, null, log)
    {
    }

    /// <summary> Test seam ctor: allows a test to inject a worker body that throws to verify a
    /// <paramref name="workerBody"/> is a fault-injection seam: the default runs the real
    /// tight burning loop; tests may substitute a body that throws to verify a worker
    /// failure degrades to <see cref="TestStatus.Failed"/> instead of ending the process.
    internal CpuStressModule(ISensorProvider sensors, Action<CancellationToken>? workerBody, IDiagnosticLog? log)
    {
        _sensors = sensors ?? throw new ArgumentNullException(nameof(sensors));
        _workerBody = workerBody ?? (token => Burn(token));
        _log = log;
    }

    public IModuleMetadata Metadata { get; } = new StressMetadata();

    public string ModuleId => "stress";

    public ModulePhase CurrentPhase { get; private set; } = ModulePhase.NotStarted;

    public bool IsRunning => CurrentPhase is ModulePhase.Setup or ModulePhase.Running or ModulePhase.AwaitingOperatorConfirmation;

    public IList<ModuleMeasurement> Measurements { get; } = [];

    public IList<string> Findings { get; } = [];

    public IList<string> OperatorActions { get; } = [];

    /// <summary>
    /// Target run duration. Bounded to <see cref="DefaultDurationSeconds"/> so the
    /// §8 cap can never be exceeded (the orchestrator timeout is a backstop for
    /// this). Set before calling <see cref="Start"/>.
    /// </summary>
    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            var capped = value <= TimeSpan.Zero ? TimeSpan.FromSeconds(DefaultDurationSeconds) : value;
            if (capped.TotalSeconds > DefaultDurationSeconds)
            {
                capped = TimeSpan.FromSeconds(DefaultDurationSeconds);
            }

            _duration = capped;
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
            _cts = new CancellationTokenSource();
            _startedAt = DateTime.UtcNow;
            CurrentPhase = ModulePhase.Running;
            Measurements.Clear();
            Findings.Clear();
            OperatorActions.Clear();

            SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED);

            int cores = Environment.ProcessorCount;
            _coreCount = cores;
            _workers = new Thread[cores];
            for (int i = 0; i < cores; i++)
            {
                var worker = new Thread(() => RunBurn(_workerBody, _cts.Token))
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal,
                    Name = $"CpuStress-{i}",
                };
                _workers[i] = worker;
                worker.Start();
            }

            Findings.Add($"Burn-in started on {cores} logical cores; target duration {_duration:g}.");

            // Live telemetry, then self-stop at the duration cap. Capture the CTS
            // so the completion continuation can prove it belongs to THIS run: a
            // stale continuation from a cancelled earlier run must never stop a
            // run that started after the cancel (restart race).
            var cts = _cts;
            _telemetryTimer = new Timer(_ => PublishTelemetry(running: true), null, 0, 1000);
            Task.Delay(_duration, cts.Token)
                .ContinueWith(_ => CompleteNaturally(cts), TaskScheduler.Default);
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

            cb = StopWorkers(TestStatus.Cancelled);
        }

        // Tear down workers outside the lock: siblings that faulted are blocked on
        // the gate and must be allowed to bail (IsRunning is now false) before we join.
        JoinWorkers();

        // Invoke the completion callback outside the lock: it re-enters the
        // orchestrator, which may concurrently hold its lock while calling
        // Cancel() on this module (AB-BA deadlock otherwise).
        cb?.Invoke(TestStatus.Cancelled);
    }

    private void CompleteNaturally(CancellationTokenSource cts)
    {
        Action<TestStatus>? cb;
        lock (_gate)
        {
            if (!IsRunning || !ReferenceEquals(cts, _cts))
            {
                // Superseded (restart raced with a cancelled run's continuation)
                // or already stopped — do not stop the current run.
                return;
            }

            cb = StopWorkers(TestStatus.Passed);
        }

        JoinWorkers();
        cb?.Invoke(TestStatus.Passed);
    }

    /// <summary>
    /// The operator's deliberate Stop button: the intended end of a shortened
    /// burn-in. Resolves as <see cref="TestStatus.Passed"/> with a finding stating
    /// the achieved duration — a planned 30-second smoke test must not read as an
    /// abandoned run (roadmap Phase 2.4).
    /// </summary>
    public void CompleteEarly()
    {
        Action<TestStatus>? cb;
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            cb = StopWorkers(TestStatus.Passed, stoppedEarly: true);
        }

        JoinWorkers();
        cb?.Invoke(TestStatus.Passed);
    }

    /// <summary>
    /// Runs <see cref="Burn"/>, converting any worker-thread exception into a clean
    /// <see cref="TestStatus.Failed"/> rather than terminating the process. An
    /// uncaught exception on a background thread would otherwise crash the app
    /// (architecture §9.7: a single failing call must degrade, not crash).
    /// </summary>
    private void RunBurn(Action<CancellationToken> workerBody, CancellationToken token)
    {
        try
        {
            workerBody(token);
        }
        catch (Exception ex)
        {
            _log?.Write($"CpuStressModule: worker failed; degrading the run to Failed — {ex.GetType().Name}: {ex.Message}");
            FailRun(ex);
        }
    }

    /// <summary>
    /// Records a worker failure. Mirrors <see cref="CompleteNaturally"/> but resolves
    /// as <see cref="TestStatus.Failed"/>; the completion callback is invoked AFTER
    /// releasing the lock, as with the other completion paths.
    /// </summary>
    private void FailRun(Exception ex)
    {
        Action<TestStatus>? cb;
        lock (_gate)
        {
            if (!IsRunning || _cts is null)
            {
                return;
            }

            cb = StopWorkers(TestStatus.Failed);
        }

        // Tear down workers outside the lock so a sibling that faulted can release
        // the gate (IsRunning is now false) instead of deadlocking the join.
        JoinWorkers();

        // The exception type/message is an internal diagnostic, not reader information;
        // RunBurn already routed it to the diagnostics log. The finding stays human-facing.
        Findings.Add("Burn-in worker failed; the run was ended early.");
        cb?.Invoke(TestStatus.Failed);
    }

    /// <summary>
    /// Tears down workers/timers, records the result, and publishes a final
    /// telemetry sample. Caller must hold <see cref="_gate"/>. Returns the
    /// completion callback; the caller invokes it AFTER releasing the lock so
    /// the callback (which may re-enter the orchestrator) can never deadlock
    /// against a concurrent Cancel holding the orchestrator lock.
    /// </summary>
    private Action<TestStatus>? StopWorkers(TestStatus finalStatus, bool stoppedEarly = false)
    {
        _telemetryTimer?.Dispose();
        _telemetryTimer = null;

        _cts?.Cancel();

        CurrentPhase = finalStatus == TestStatus.Passed ? ModulePhase.Complete : ModulePhase.Cancelled;
        Findings.Add(stoppedEarly
            ? $"Burn-in stopped by the operator after {(DateTime.UtcNow - _startedAt):g} of the {_duration:g} target."
            : finalStatus == TestStatus.Passed
                ? $"Burn-in completed the full target duration of {_duration:g}."
                : "Burn-in stopped before completing the target duration.");

        PublishTelemetry(running: false, finalStatus);
        var cb = _onComplete;
        _onComplete = null;

        try
        {
            SetThreadExecutionState(ES_CONTINUOUS);
        }
        catch
        {
            // Best-effort: restore normal power policy.
        }

        return cb;
    }

    private void JoinWorkers()
    {
        foreach (var worker in _workers)
        {
            if (ReferenceEquals(worker, Thread.CurrentThread))
            {
                // Never join the thread we're currently running on (would deadlock).
                continue;
            }

            try
            {
                worker.Join(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Best-effort join on shutdown.
            }
        }

        _workers = [];
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Tight arithmetic loop to keep each core saturated. The result is consumed
    /// so the JIT cannot optimize the loop away.
    /// </summary>
    private static void Burn(CancellationToken token)
    {
        double accumulator = 1.0;
        while (!token.IsCancellationRequested)
        {
            for (int k = 0; k < 20000; k++)
            {
                accumulator = Math.Sqrt(accumulator + (k * 0.0001)) + Math.Sin(k);
            }

            // Tiny yield so a hard spin doesn't completely starve other
            // BelowNormal work on the same core; the core is still loaded.
            if ((k_yield++ & 0x3FF) == 0)
            {
                Thread.Yield();
            }
        }

        _ = accumulator;
    }

    // Local counter for the occasional yield above (avoids a field on the
    // module shared across threads).
    [ThreadStatic]
    private static int k_yield;

    private void PublishTelemetry(bool running, TestStatus? final = null)
    {
        double? load = null;
        var temps = new List<float?>();
        string? sensorUnavailableReason = _sensors.UnavailableReason;
        try
        {
            foreach (var reading in _sensors.ReadAll())
            {
                bool cpu = reading.SensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                           reading.HardwareName.Contains("CPU", StringComparison.OrdinalIgnoreCase);
                if (!cpu)
                {
                    continue;
                }

                if (reading.SensorType == "Temperature")
                {
                    temps.Add(reading.Value);
                }
                else if (reading.SensorType == "Load" &&
                         reading.SensorName.Contains("Total", StringComparison.OrdinalIgnoreCase))
                {
                    load = reading.Value;
                }
            }
        }
        catch
        {
            // Sensor read is best-effort; telemetry degrades to N/A.
        }

        WeakReferenceMessenger.Default.Send(new Messages.StressTelemetryMessage
        {
            CoreCount = _coreCount,
            Elapsed = _startedAt == default ? TimeSpan.Zero : DateTime.UtcNow - _startedAt,
            TargetDuration = _duration,
            CpuLoadPercent = load,
            CoreTempsCelsius = temps,
            Running = running,
            FinalStatus = final,
            SensorUnavailableReason = sensorUnavailableReason,
        });
    }

    private sealed class StressMetadata : IModuleMetadata
    {
        public string Id => "stress";
        public string DisplayName => "CPU Stress Test";
        public string Description => "Fixed-duration burn-in across all cores.";
        public string Category => "stress";
        public string[] RequiredCapabilities => [];
        public bool IsExclusive => true;
        public TimeSpan? MaxDuration => TimeSpan.FromSeconds(DefaultDurationSeconds + 10);
    }
}
