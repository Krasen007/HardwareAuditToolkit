using System.Diagnostics;
using System.IO;
using System.Windows;
using HardwareAuditToolkit.Core.Reporting;

namespace HardwareAuditToolkit.App.Views;

/// <summary>
/// Modal shown after an export attempt (§10 Phase 6). Leads with the HTML report path —
/// the human deliverable (roadmap C7) — and lists the JSON data path beneath it, letting
/// the operator open the HTML in the default browser or reveal the folder in Explorer.
/// A clipboard-only success (no file) shows that state explicitly.
/// </summary>
public sealed partial class ExportResultDialog : Window
{
    /// <summary>The HTML report path — the primary "Saved to" line.</summary>
    public string ReportPath { get; }

    /// <summary>The JSON data path, shown as the secondary line.</summary>
    public string JsonPath { get; }

    public bool HasFile { get; }

    public ExportResultDialog(string? jsonPath, string? htmlPath)
    {
        Application.LoadComponent(this, new Uri("/Views/ExportResultDialog.xaml", UriKind.Relative));

        ReportPath = htmlPath ?? jsonPath ?? "(saved to clipboard)";
        JsonPath = jsonPath ?? "(not saved)";
        HasFile = (htmlPath is not null && File.Exists(htmlPath)) ||
                  (jsonPath is not null && File.Exists(jsonPath));
        _htmlPath = htmlPath;
        _jsonPath = jsonPath;
        DataContext = this;
    }

    private readonly string? _htmlPath;
    private readonly string? _jsonPath;

    private void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_htmlPath) && File.Exists(_htmlPath))
        {
            Open(_htmlPath);
        }
        else if (!string.IsNullOrEmpty(_jsonPath) && File.Exists(_jsonPath))
        {
            Open(_jsonPath);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        string? target = (_htmlPath is not null && File.Exists(_htmlPath)) ? _htmlPath
            : (_jsonPath is not null && File.Exists(_jsonPath)) ? _jsonPath
            : null;
        if (target is not null)
        {
            // /select highlights the file in an Explorer window.
            Open("explorer.exe", $"/select,\"{target}\"");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Best effort — the operator can still copy the path from the dialog.
        }
    }

    private static void Open(string exe, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = true });
        }
        catch
        {
            // Best effort.
        }
    }

    /// <summary>Shows the result dialog owned by the main window.</summary>
    public static void ShowResult(ReportExportResult result)
    {
        var dialog = new ExportResultDialog(result.JsonPath, result.HtmlPath);
        if (Application.Current?.MainWindow is Window owner)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
    }
}
