using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Core.Reporting;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// The dashboard. Roadmap E1: the card list is generated from the orchestrator's
/// module roster (<c>IModuleMetadata</c>) — the single source of truth — and E3: each
/// card carries the module's current session status so the operator sees the audit's
/// shape (what ran, what passed, what's left) before exporting.
/// </summary>
public sealed class DashboardViewModel(TestOrchestrator orchestrator, AuditSession session, INavigationService navigation) : ObservableObject
{
    public ObservableCollection<DashboardItemViewModel> Modules { get; } =
        [.. orchestrator.Modules.Select(m => new DashboardItemViewModel(
            m.ModuleId,
            m.Metadata.DisplayName,
            m.Metadata.Description,
            m.Metadata.Category,
            m.Metadata.IsExclusive,
            StatusFor(m.ModuleId, session),
            navigation))];

    /// <summary>
    /// The module's session status for its card: latest recorded result, or
    /// "Running" when any run of this module is still in progress, or "Not run".
    /// </summary>
    private static string StatusFor(string moduleId, AuditSession session)
    {
        var results = session.Modules
            .Where(m => string.Equals(m.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (results.Count == 0)
        {
            return StatusDisplay.Name(TestStatus.NotRun);
        }

        if (results.Any(r => r.Status == TestStatus.Running))
        {
            return StatusDisplay.Name(TestStatus.Running);
        }

        return StatusDisplay.Name(results[^1].Status);
    }
}
