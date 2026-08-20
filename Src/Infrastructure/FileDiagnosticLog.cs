using System.Diagnostics;

namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// <see cref="IDiagnosticLog"/> that appends timestamped lines to a file next to
/// the audit session, defaulting to <c>%LOCALAPPDATA%\HardwareAuditToolkit\diagnostics.log</c>.
/// The production/published build has no attached debugger, so the
/// <c>Debug.WriteLine(true)</c> calls that otherwise guard the Phase 7 fault paths
/// write to nothing; this makes those degradations observable after the fact.
/// Every write is best-effort — a failing volume must never throw into the
/// fault-handling path that logged it (architecture §9.7).
/// </summary>
public sealed class FileDiagnosticLog(string path) : IDiagnosticLog
{
    private readonly string _path = path;
    private readonly object _gate = new();

    public FileDiagnosticLog() : this(DefaultPath())
    {
    }

    public void Write(string message)
    {
        try
        {
            string line = $"{DateTime.UtcNow:u}  {message}\n";
            lock (_gate)
            {
                string? dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Append, capping the file so it cannot grow without bound. On
                // rotation we drop the older history and keep only the newest lines.
                string previous = File.Exists(_path) ? File.ReadAllText(_path) : string.Empty;
                string combined = previous + line;
                if (combined.Length > MaxDiagnosticBytes)
                {
                    // Drop the oldest history, retaining the newest lines (tail).
                                        combined = combined[^MaxDiagnosticBytes..];
                }

                File.WriteAllText(_path, combined);
            }
        }
        catch
        {
            // Diagnostics must never throw into the caller path that is handling a fault.
        }
    }

    /// <summary>Default diagnostics location under the user's app-data folder.</summary>
    public static string DefaultPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HardwareAuditToolkit", "diagnostics.log");

    private const int MaxDiagnosticBytes = 512 * 1024;
}