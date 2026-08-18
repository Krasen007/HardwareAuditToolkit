using System.Diagnostics;
using System.IO;

namespace HardwareAuditToolkit.App;

/// <summary>
/// <para>
/// §9.1: redirects the .NET single-file extraction directory away from %TEMP%
/// to a fixed, predictable path so security teams can allow-list one location.
/// </para>
/// <para>
/// The runtime host reads <c>DOTNET_BUNDLE_EXTRACT_BASE_DIR</c> at process
/// start (dotnet/runtime bundle extractor), so this takes effect from the NEXT
/// launch onward — the very first launch still extracts to %TEMP% and is
/// documented in docs\DeploymentNote.md. We never override a value that is
/// already configured (e.g. one provided by group policy).
/// </para>
/// </summary>
public static class BundleExtractionBootstrap
{
    public const string EnvironmentVariableName = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";
    public const string RelativeExtractionRoot = @"HardwareAuditToolkit\extract";

    /// <summary>Best-effort and idempotent; never throws.</summary>
    public static void EnsureExtractionDirectoryRedirected()
    {
        if (IsEnvironmentVariableDefined())
        {
            return;
        }

        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            RelativeExtractionRoot);

        try
        {
            Directory.CreateDirectory(baseDir);
            Environment.SetEnvironmentVariable(EnvironmentVariableName, baseDir, EnvironmentVariableTarget.User);
        }
        catch (Exception ex)
        {
            // Non-fatal: the app still runs; only the allow-list path is affected.
            Debug.WriteLine($"BundleExtractionBootstrap: failed to configure '{EnvironmentVariableName}' ({ex.Message})");
        }
    }

    private static bool IsEnvironmentVariableDefined()
    {
        foreach (EnvironmentVariableTarget target in new[]
                 {
                     EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Machine,
                 })
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariableName, target)))
            {
                return true;
            }
        }

        return false;
    }
}
