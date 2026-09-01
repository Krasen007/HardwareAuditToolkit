using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareAuditToolkit.App.Services;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// A single selectable module entry on the dashboard, generated from the module's
/// <c>IModuleMetadata</c> (roadmap E1) plus its current session status (E3).
/// </summary>
public sealed partial class DashboardItemViewModel(
    string moduleId,
    string displayName,
    string description,
    string category,
    bool isExclusive,
    string statusText,
    INavigationService navigation) : ObservableObject
{
    public string ModuleId { get; } = moduleId;
    public string DisplayName { get; } = displayName;
    public string Description { get; } = description;
    public string Category { get; } = category;
    public bool IsExclusive { get; } = isExclusive;

    /// <summary>Display status for this card: "Not run", "Passed", "Failed", … (E3).</summary>
    public string StatusText { get; } = statusText;

    [RelayCommand]
    private void Open()
        => navigation.NavigateToModule(ModuleId);
}
