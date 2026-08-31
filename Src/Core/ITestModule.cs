namespace HardwareAuditToolkit.Core;

/// <summary>
/// Contract implemented by every test module (§5). Modules are discovered by
/// the shell via DI and coordinated by <see cref="TestOrchestrator"/>.
/// </summary>
public interface ITestModule
{
    /// <summary>Static metadata about this module.</summary>
    IModuleMetadata Metadata { get; }

    /// <summary>Unique identifier (matches <see cref="IModuleMetadata.Id"/>).</summary>
    string ModuleId { get; }

    /// <summary>Current lifecycle phase (§5 workflow).</summary>
    ModulePhase CurrentPhase { get; }

    /// <summary>True while the module is between <see cref="ModulePhase.Setup"/> and
    /// <see cref="ModulePhase.Complete"/>/<see cref="ModulePhase.Cancelled"/>.</summary>
    bool IsRunning { get; }

    /// <summary>Live typed measurements streamed while the module runs.</summary>
    IList<ModuleMeasurement> Measurements { get; }

    /// <summary>Structured, human-readable findings added to the report.</summary>
    IList<string> Findings { get; }

    /// <summary>Operator actions logged by the module (checkpoint confirmations, etc.).</summary>
    IList<string> OperatorActions { get; }

    /// <summary>Returns true when the module is ready to start
    /// (e.g. no other exclusive module is currently running).</summary>
    bool CheckPreconditions();

    /// <summary>
    /// Starts the module. Must return promptly; work runs asynchronously.
    /// </summary>
    /// <param name="onComplete">Callback invoked exactly once when the module
    /// finishes, with its final status.</param>
    void Start(Action<TestStatus> onComplete);

    /// <summary>Stops a running module asynchronously; the completion callback
    /// is expected to fire with <see cref="TestStatus.Cancelled"/>.</summary>
    void Cancel();
}
