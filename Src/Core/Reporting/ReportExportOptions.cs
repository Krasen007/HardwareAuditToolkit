namespace HardwareAuditToolkit.Core.Reporting;

/// <summary>
/// Options controlling a single export attempt (architecture §10 Phase 6). The cascade
/// itself lives in <see cref="SessionExporter"/>; the UI-bound steps (manual folder
/// picker, clipboard fallback) are supplied as callbacks so the pure export logic stays
/// unit-testable without any WPF/Win32 dependency.
/// </summary>
public sealed class ReportExportOptions
{
    /// <summary>
    /// Directories to attempt in order before falling back to the manual picker
    /// (architecture §9.6 steps 1–3: portable app dir, Desktop, %TEMP%).
    /// </summary>
    public IReadOnlyList<string> PreferredDirectories { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Step 4 (§9.6): invoked when no preferred directory is writable. Should present a
    /// "choose a folder" picker and return the chosen directory, or <c>null</c> if the
    /// operator cancels. May be <c>null</c> when no UI is available (e.g. headless).
    /// </summary>
    public Func<string?>? RequestManualFolder { get; init; }

    /// <summary>
    /// Step 5 (§9.6, last resort): invoked when no location could be written. Receives the
    /// serialized audit JSON and should copy it to the clipboard / show a modal. Return
    /// <c>true</c> when the operator acknowledged, so the in-memory data is considered
    /// safe even though no file was written. May be <c>null</c> when no UI is available.
    /// </summary>
    public Func<string, bool>? ShowClipboardFallback { get; init; }
}
