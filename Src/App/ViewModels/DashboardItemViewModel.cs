using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareAuditToolkit.App.Services;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// A single selectable module entry on the dashboard (Phase 1 lists the planned
/// modules; the real <see cref="Core.ITestModule"/> implementations land in
/// later phases and will replace these stubs).
/// </summary>
public sealed partial class DashboardItemViewModel : ObservableObject
{
    public string ModuleId { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string Category { get; }
    public bool IsExclusive { get; }

    [RelayCommand]
    private void Open()
        => _navigation.NavigateToModule(ModuleId);

    private readonly INavigationService _navigation;

    public DashboardItemViewModel(
        string moduleId,
        string displayName,
        string description,
        string category,
        bool isExclusive,
        INavigationService navigation)
    {
        ModuleId = moduleId;
        DisplayName = displayName;
        Description = description;
        Category = category;
        IsExclusive = isExclusive;
        _navigation = navigation;
    }
}
