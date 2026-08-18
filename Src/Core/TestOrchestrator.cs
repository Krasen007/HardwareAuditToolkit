namespace HardwareAuditToolkit.Core;

/// <summary>
/// Coordinates test modules for a single audit session (§4).
///
/// Rules enforced here:
///  - Exclusive modules (keyboard, mouse, monitor, CPU stress) run strictly one
///    at a time, sequentially. A module advertises this via
///    <see cref="IModuleMetadata.IsExclusive"/>.
///  - Non-exclusive modules may overlap with each other and with the exclusive
///    module (e.g. a system-info snapshot while a keyboard test runs).
///  - Every module that declares a <see cref="IModuleMetadata.MaxDuration"/> is
///    force-cancelled and recorded as <see cref="TestStatus.Cancelled"/> if it
///    exceeds that budget (§6 unattended-run timeout).
///  - Each run appends exactly one <see cref="ModuleResult"/> to the session;
///    the running result is updated in place when the module completes.
/// </summary>
public sealed class TestOrchestrator : IDisposable
{
    private readonly AuditSession _session;
    private readonly Dictionary<string, ITestModule> _modulesById;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();

    // At most one exclusive module may be running at a time.
    private ITestModule? _exclusiveModule;

    // All currently running modules, keyed by ModuleId.
    private readonly Dictionary<string, RunningEntry> _running = new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    /// <summary>All modules the orchestrator knows about, in registration order.</summary>
    public IReadOnlyList<ITestModule> Modules { get; }

    /// <summary>Modules currently running.</summary>
    public IReadOnlyList<ITestModule> RunningModules
    {
        get
        {
            lock (_gate)
            {
                return _running.Values.Select(e => e.Module).ToList();
            }
        }
    }

    /// <summary>The exclusive module currently running, if any.</summary>
    public ITestModule? CurrentExclusiveModule
    {
        get
        {
            lock (_gate)
            {
                return _exclusiveModule;
            }
        }
    }

    public TestOrchestrator(AuditSession session, IEnumerable<ITestModule> modules)
        : this(session, modules, TimeProvider.System)
    {
    }

    public TestOrchestrator(AuditSession session, IEnumerable<ITestModule> modules, TimeProvider timeProvider)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        var list = modules?.ToList() ?? new List<ITestModule>();
        Modules = list.AsReadOnly();
        _modulesById = new Dictionary<string, ITestModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in list)
        {
            if (!string.IsNullOrWhiteSpace(module.ModuleId) && !_modulesById.ContainsKey(module.ModuleId))
            {
                _modulesById.Add(module.ModuleId, module);
            }
        }
    }

    /// <summary>
    /// Starts a module, enforcing exclusivity, preconditions, and single-start.
    /// </summary>
    /// <returns>True when the module was started; otherwise <paramref name="reason"/>
    /// explains why.</returns>
    public bool TryStartModule(string moduleId, out string reason)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (!_modulesById.TryGetValue(moduleId, out var module))
            {
                reason = $"Unknown module '{moduleId}'.";
                return false;
            }

            if (_running.ContainsKey(module.ModuleId))
            {
                reason = $"Module '{moduleId}' is already running.";
                return false;
            }

            if (!module.CheckPreconditions())
            {
                reason = $"Preconditions for module '{moduleId}' are not met.";
                return false;
            }

            if (module.Metadata.IsExclusive && _exclusiveModule is not null)
            {
                reason = $"Exclusive module '{_exclusiveModule.ModuleId}' is already running; exclusive modules run one at a time.";
                return false;
            }

            var result = new ModuleResult
            {
                ModuleId = module.ModuleId,
                DisplayName = module.Metadata.DisplayName,
                Status = TestStatus.Running,
                StartedAt = _timeProvider.GetUtcNow().UtcDateTime,
            };
            _session.Modules.Add(result);

            if (module.Metadata.IsExclusive)
            {
                _exclusiveModule = module;
            }

            ITimer? timer = null;
            if (module.Metadata.MaxDuration is { } maxDuration && maxDuration > TimeSpan.Zero)
            {
                timer = _timeProvider.CreateTimer(OnModuleTimedOut, module.ModuleId, maxDuration, Timeout.InfiniteTimeSpan);
            }

            _running.Add(module.ModuleId, new RunningEntry(module, result, timer));

            try
            {
                module.Start(status => OnModuleCompleted(module, status));
            }
            catch (Exception)
            {
                // A module that throws from Start must not be left wedged in the
                // running set — record it as a failed start.
                if (_running.TryGetValue(module.ModuleId, out var entry) && ReferenceEquals(entry.Module, module))
                {
                    timer?.Dispose();
                    _running.Remove(module.ModuleId);
                    if (ReferenceEquals(module, _exclusiveModule))
                    {
                        _exclusiveModule = null;
                    }

                    result.Status = TestStatus.Failed;
                    result.CompletedAt = _timeProvider.GetUtcNow().UtcDateTime;
                    result.Findings.Add("Module.Start threw an exception; the module failed before it could begin.");
                    UpdateOverallStatus();
                }

                reason = $"Module '{moduleId}' failed to start.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }

    /// <summary>
    /// Cancels a specific running module. Returns false when no such module is running.
    /// </summary>
    public bool CancelModule(string moduleId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (!_running.TryGetValue(moduleId, out var entry))
            {
                return false;
            }

            entry.Module.Cancel();

            // If the module reports completion through its callback, it has
            // already been removed and recorded; otherwise record the cancel here.
            if (_running.TryGetValue(moduleId, out var stillRunning) && ReferenceEquals(stillRunning.Module, entry.Module))
            {
                CompleteCancelledEntry(entry, "Cancelled by operator.");
            }

            return true;
        }
    }

    /// <summary>Cancels every module currently running.</summary>
    public void CancelAll()
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            foreach (var entry in _running.Values.ToList())
            {
                entry.Module.Cancel();
                if (_running.TryGetValue(entry.Module.ModuleId, out var stillRunning) && ReferenceEquals(stillRunning.Module, entry.Module))
                {
                    CompleteCancelledEntry(entry, "Cancelled by operator.");
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var entry in _running.Values)
            {
                try
                {
                    entry.Module.Cancel();
                }
                catch
                {
                    // Best effort during shutdown.
                }

                entry.Timer?.Dispose();
            }

            _running.Clear();
            _exclusiveModule = null;
        }
    }

    private void OnModuleCompleted(ITestModule module, TestStatus status)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (!_running.TryGetValue(module.ModuleId, out var entry) || !ReferenceEquals(entry.Module, module))
            {
                return; // Stale callback (e.g. raced with a timeout) — already recorded.
            }

            entry.Timer?.Dispose();
            _running.Remove(module.ModuleId);
            if (ReferenceEquals(module, _exclusiveModule))
            {
                _exclusiveModule = null;
            }

            var result = entry.Result;
            result.Status = status;
            result.CompletedAt = _timeProvider.GetUtcNow().UtcDateTime;
            result.Findings.AddRange(module.Findings);
            result.Measurements.AddRange(module.Measurements);
            result.OperatorActions.AddRange(module.OperatorActions);
            result.Artifacts.AddRange(module.Artifacts);

            UpdateOverallStatus();
        }
    }

    private void OnModuleTimedOut(object? state)
    {
        string moduleId = (string)state!;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (!_running.TryGetValue(moduleId, out var entry))
            {
                return;
            }

            entry.Module.Cancel();
            CompleteCancelledEntry(entry, $"Module exceeded its maximum duration of {entry.Module.Metadata.MaxDuration} and was force-cancelled.");
        }
    }

    private void CompleteCancelledEntry(RunningEntry entry, string reason)
    {
        entry.Timer?.Dispose();
        _running.Remove(entry.Module.ModuleId);
        if (ReferenceEquals(entry.Module, _exclusiveModule))
        {
            _exclusiveModule = null;
        }

        var result = entry.Result;
        result.Status = TestStatus.Cancelled;
        result.CompletedAt = _timeProvider.GetUtcNow().UtcDateTime;
        result.Findings.Add(reason);
        result.Findings.AddRange(entry.Module.Findings);
        result.Measurements.AddRange(entry.Module.Measurements);
        result.OperatorActions.AddRange(entry.Module.OperatorActions);
        result.Artifacts.AddRange(entry.Module.Artifacts);

        UpdateOverallStatus();
    }

    private void UpdateOverallStatus()
    {
        var completed = _session.Modules.Where(m => m.CompletedAt.HasValue).ToList();
        if (completed.Count == 0)
        {
            _session.OverallStatus = TestStatus.NotRun;
            return;
        }

        if (completed.Any(m => m.Status == TestStatus.Failed))
        {
            _session.OverallStatus = TestStatus.Failed;
        }
        else if (completed.Any(m => m.Status == TestStatus.Warning || m.Status == TestStatus.Unsupported))
        {
            _session.OverallStatus = TestStatus.Warning;
        }
        else if (completed.Any(m => m.Status == TestStatus.Cancelled))
        {
            _session.OverallStatus = TestStatus.Cancelled;
        }
        else if (completed.All(m => m.Status == TestStatus.Passed || m.Status == TestStatus.Skipped))
        {
            _session.OverallStatus = TestStatus.Passed;
        }
        else
        {
            _session.OverallStatus = TestStatus.NotRun;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TestOrchestrator));
        }
    }

    private sealed record RunningEntry(ITestModule Module, ModuleResult Result, ITimer? Timer);
}
