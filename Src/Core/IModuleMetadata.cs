namespace HardwareAuditToolkit.Core;

/// <summary>
/// Static metadata for a test module, used by the orchestrator and the shell.
/// </summary>
public interface IModuleMetadata
{
    /// <summary>Unique, stable identifier for the module.</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in the dashboard.</summary>
    string DisplayName { get; }

    /// <summary>What the module tests.</summary>
    string Description { get; }

    /// <summary>Free-form category (e.g. "keyboard", "mouse", "monitor", "system", "stress").</summary>
    string Category { get; }

    /// <summary>
    /// Capabilities required by this module (e.g. "raw keyboard input", "DDC/CI").
    /// Always satisfiable without elevation in v1; the field exists so v2's
    /// admin-gated capabilities slot in cleanly.
    /// </summary>
    string[] RequiredCapabilities { get; }

    /// <summary>
    /// True when the module must run alone (keyboard, mouse, monitor, CPU stress).
    /// The orchestrator enforces that at most one exclusive module runs at a time (§4).
    /// </summary>
    bool IsExclusive { get; }

    /// <summary>
    /// Hard time budget for an unattended run. When exceeded, the orchestrator
    /// force-cancels the module and records <see cref="TestStatus.Cancelled"/> (§6).
    /// Null means "no timeout".
    /// </summary>
    TimeSpan? MaxDuration { get; }
}

/// <summary>
/// Result status of a test module execution (§4).
/// </summary>
public enum TestStatus
{
    /// <summary>The module hasn't been started this session.</summary>
    NotRun,

    /// <summary>In progress.</summary>
    Running,

    /// <summary>Met pass criteria, or the operator confirmed OK.</summary>
    Passed,

    /// <summary>Did not meet pass criteria, or the operator flagged a defect.</summary>
    Failed,

    /// <summary>Completed with a flagged concern that isn't a clean pass or fail.</summary>
    Warning,

    /// <summary>The operator or timeout budget chose not to run it.</summary>
    Skipped,

    /// <summary>The required capability isn't present on this hardware.</summary>
    Unsupported,

    /// <summary>The operator or orchestrator stopped it mid-run.</summary>
    Cancelled,
}
