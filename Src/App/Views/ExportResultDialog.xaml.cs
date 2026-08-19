using System.Diagnostics;
using System.IO;
using System.Windows;
using HardwareAuditToolkit.Core.Reporting;

namespace HardwareAuditToolkit.App.Views;

/// <summary>
/// Modal shown after a successful export (§10 Phase 6). Displays the saved location and
/// lets the operator open the HTML report in the default browser or reveal the file in
/// File Explorer, so the result is immediately verifiable rather than just acknowledged.
/// </summary>
public sealed partial class ExportResultDialog : Window
{
    public string JsonPath { get; }

    public bool HasFile { get; }

    public ExportResultDialog(string? jsonPath, string? htmlPath)
    {
        Application.LoadComponent(this, new Uri("/Views/ExportResultDialog.xaml", UriKind.Relative));

        JsonPath = jsonPath ?? "(saved to clipboard)";
        HasFile = !string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath);
        HtmlPath = htmlPath;
        DataContext = this;
    }

    private string? HtmlPath { get; }

    private void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(HtmlPath) && File.Exists(HtmlPath))
        {
            Open(HtmlPath);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(JsonPath) && JsonPath != "(saved to clipboard)" && File.Exists(JsonPath))
        {
            // /select, highlights the file in an Explorer window.
            Open("explorer.exe", $"/select,\"{JsonPath}\"");
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
