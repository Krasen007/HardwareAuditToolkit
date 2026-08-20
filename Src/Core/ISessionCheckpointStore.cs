namespace HardwareAuditToolkit.Core;

/// <summary>
/// <para>
/// Persists a durable checkpoint of the in-memory <see cref="AuditSession"/> as JSON
/// so a forced termination / process fault cannot lose findings collected before an
/// explicit §9.6 export. The orchestrator calls <see cref="Save"/> after each module
/// completes and on shutdown.
/// </para>
/// <para>
/// Best-effort: a checkpoint failure must never throw into (or block) the
/// module-completion path that triggered it (architecture §7 / §9.7).
/// </para>
/// </summary>
public interface ISessionCheckpointStore
{
    /// <summary>Writes the current session state to durable storage. Must not throw.</summary>
    void Save(AuditSession session);
}