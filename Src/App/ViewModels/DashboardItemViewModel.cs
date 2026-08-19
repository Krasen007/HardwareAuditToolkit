using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareAuditToolkit.App.Services;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// A single selectable module entry on the dashboard (Phase 1 lists the planned
/// modules; the real <see cref="Core.ITestModule"/> implementations land in
/// later phases and will replace these stubs).
/// </summary>
public sealed partial class DashboardItemViewModel(
    string moduleId,
    string displayName,
    string description,
    string category,
    bool isExclusive,
    INavigationService navigation) : ObservableObject
{
    public string ModuleId { get; } = moduleId;
    public string DisplayName { get; } = displayName;
    public string Description { get; } = description;
    public string Category { get; } = category;
    public bool IsExclusive { get; } = isExclusive;

    [RelayCommand]
    private void Open()
        => navigation.NavigateToModule(ModuleId);
}
