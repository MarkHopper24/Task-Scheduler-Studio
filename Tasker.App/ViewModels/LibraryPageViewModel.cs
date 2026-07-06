using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tasker_App.Services;

namespace Tasker_App.ViewModels;

/// <summary>
/// Backs the Library page: holds the combined script catalog (built-ins + the user's saved scripts)
/// and exposes a category- and search-filtered view.
/// </summary>
public partial class LibraryPageViewModel : ObservableObject
{
    public ObservableCollection<ScriptTemplate> Filtered { get; } = new();

    /// <summary>Category chips shown in the filter bar ("All" plus each category).</summary>
    public IReadOnlyList<string> Categories { get; } = new[]
    {
        "All", "Cleanup", "Backup", "Maintenance", "Monitoring", "System",
    };

    private List<ScriptTemplate> _all = new();

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = "All";

    /// <summary>Reloads the full library from disk and reapplies the current filter.</summary>
    public void Reload()
    {
        _all = ScriptLibraryStore.All();
        ApplyFilter();
    }

    public void ApplyFilter()
    {
        IEnumerable<ScriptTemplate> view = _all;

        if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != "All")
            view = view.Where(s => s.CategoryName == SelectedCategory);

        var q = SearchText?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            view = view.Where(s =>
                s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.CategoryName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        Filtered.Clear();
        foreach (var s in view) Filtered.Add(s);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
}
