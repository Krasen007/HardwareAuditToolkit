using System.IO;
using System.Windows;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Core.Reporting;
using Microsoft.Win32;

namespace HardwareAuditToolkit.App.Services;

/// <summary>
/// App-side bridge between the pure <see cref="SessionExporter"/> (Core) and the WPF
/// shell (architecture §10 Phase 6, §9.6). Supplies the cascade's UI-bound steps — the
/// manual folder picker (step 4) and the clipboard last-resort modal (step 5) — and
/// records the session's completion timestamp when an export is triggered. Everything
/// testable about the cascade lives in <see cref="SessionExporter"/>; this class only
/// provides the Windows interactions.
/// </summary>
public sealed class ReportExportService
{
    private readonly SessionExporter _exporter;
    private readonly AuditSession _session;

    public ReportExportService(SessionExporter exporter, AuditSession session)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>
    /// Runs the full export cascade for the current session. Marks the session completed
    /// (if not already) so the report carries a finished timestamp, then delegates to the
    /// cascade with the standard §9.6 candidate locations.
    /// </summary>
    public ReportExportResult Export()
    {
        if (_session.CompletedAt is null)
        {
            _session.CompletedAt = DateTime.UtcNow;
        }

        var options = new ReportExportOptions
        {
            PreferredDirectories = BuildPreferredDirectories(),
            RequestManualFolder = ShowFolderPicker,
            ShowClipboardFallback = ShowClipboardFallback,
        };

        return _exporter.Export(_session, options);
    }

    /// <summary>
    /// §9.6 steps 1–3: portable app directory (next to the .exe), Desktop, then %TEMP%.
    /// </summary>
    private static IReadOnlyList<string> BuildPreferredDirectories()
    {
        var dirs = new List<string>(3);

        string? appDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrEmpty(appDir))
        {
            appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        dirs.Add(appDir);

        dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        dirs.Add(Path.GetTempPath());

        return dirs;
    }

    private static string? ShowFolderPicker()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder to save the audit report",
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static bool ShowClipboardFallback(string json)
    {
        try
        {
            Clipboard.SetText(json);
        }
        catch
        {
            // Even if the clipboard is unavailable, we still surface the modal below.
        }

        MessageBox.Show(
            "No writable location was found for the audit report.\nThe audit JSON has been copied to the clipboard — paste it into a file to preserve the record.",
            "Audit Report — Clipboard Fallback",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return true;
    }
}
