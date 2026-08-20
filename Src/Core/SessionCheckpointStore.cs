using System.Text.Json;

namespace HardwareAuditToolkit.Core;

/// <summary>
/// File-backed <see cref="ISessionCheckpointStore"/>. Serializes the session with the
/// same indented JSON shape as <see cref="Reporting.SessionExporter"/> and writes it to a
/// deterministic path under the user's app-data folder:
/// <c>%LOCALAPPDATA%\HardwareAuditToolkit\audit-{sessionId}.hat.json</c>. Because a
/// checkpoint is a snapshot (not the final report), it is written directly and
/// best-effort; any I/O failure is swallowed so it can never break the module-completion
/// path that triggered the save.
/// </summary>
public sealed class SessionCheckpointStore(string directory) : ISessionCheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory = directory;

    public SessionCheckpointStore() : this(DefaultDirectory())
    {
    }

    public void Save(AuditSession session)
    {
        if (session is null)
        {
            return; // best-effort; never throw into the completion path (§9.7).
        }

        try
        {
            Directory.CreateDirectory(_directory);
            string target = Path.Combine(_directory, $"audit-{Sanitize(session.SessionId)}.hat.json");
            File.WriteAllText(target, JsonSerializer.Serialize(session, JsonOptions));
        }
        catch
        {
            // Best-effort checkpoint — never throw into the completion path.
        }
    }

    /// <summary>
    /// Default checkpoint location: <c>%LOCALAPPDATA%\HardwareAuditToolkit</c>, kept
    /// alongside the diagnostics log and inside the same predictable path a security
    /// team allow-lists (§9.1).
    /// </summary>
    public static string DefaultDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "HardwareAuditToolkit");
    }

    private static string Sanitize(string value)
    {
        string result = value;
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(result) ? "session" : result;
    }
}