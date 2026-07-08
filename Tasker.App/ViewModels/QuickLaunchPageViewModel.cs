using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Tasker.Core;
using Tasker_App.Services;

namespace Tasker_App.ViewModels;

/// <summary>
/// Backs the Quick Launch gallery: a curated set of pinned, enabled tasks shown as cards that
/// can be run with a single click. The pinned set is persisted in <see cref="AppSettings"/>.
/// </summary>
public partial class QuickLaunchPageViewModel : ObservableObject
{
    public ObservableCollection<TaskSummaryDto> Items { get; } = new();

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool StatusOpen { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; } = InfoBarSeverity.Success;

    /// <summary>When true, the page shows the compact floating-widget layout (just run buttons).</summary>
    [ObservableProperty]
    public partial bool IsWidget { get; set; }

    public bool IsNormal => !IsWidget;

    public bool IsEmpty => Items.Count == 0 && !IsBusy;

    partial void OnIsWidgetChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNormal));
        WidgetMode.Set(value);
    }

    [RelayCommand]
    private void ToggleWidget() => IsWidget = !IsWidget;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        OnPropertyChanged(nameof(IsEmpty));
        try
        {
            var pinned = AppSettings.QuickLaunch;
            Items.Clear();
            if (pinned.Count > 0)
            {
                var all = await TaskerClient.ListTasksAsync("\\", recursive: true);
                var byPath = new Dictionary<string, TaskSummaryDto>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in all) byPath[t.Path] = t;

                var onlyTasker = AppSettings.OnlyTaskerTasks;
                var kept = new List<string>();
                foreach (var p in pinned)
                {
                    if (byPath.TryGetValue(p, out var dto))
                    {
                        kept.Add(p); // task still exists -> keep the pin even if the filter hides it
                        if (!onlyTasker || dto.CreatedByTasker)
                            Items.Add(dto);
                    }
                }
                // Drop any pins whose task was deleted/renamed so the gallery stays clean.
                if (kept.Count != pinned.Count) AppSettings.SetQuickLaunch(kept);
            }
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    public async Task RunAsync(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var name = Items.FirstOrDefault(i => i.Path == path)?.Name ?? path;
        var result = await TaskerClient.RunTaskAsync(path);
        ShowStatus(result.Success ? $"Started \u201C{name}\u201D." : result.Message, isError: !result.Success);

        if (result.Success)
        {
            // Give the engine a moment to record the run, then refresh just this card's
            // last-run/state in place (replacing the item updates only its tile, no flicker).
            await Task.Delay(800);
            try
            {
                var all = await TaskerClient.ListTasksAsync("\\", recursive: true);
                var updated = all.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
                var idx = -1;
                for (var i = 0; i < Items.Count; i++)
                {
                    if (string.Equals(Items[i].Path, path, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                }
                if (updated is not null && idx >= 0) Items[idx] = updated;
            }
            catch { /* best-effort refresh */ }
        }
    }

    [RelayCommand]
    public void Unpin(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        AppSettings.RemoveQuickLaunch(path);
        if (Items.FirstOrDefault(i => i.Path == path) is { } item) Items.Remove(item);
        OnPropertyChanged(nameof(IsEmpty));
        ShowStatus("Removed from Quick Launch.", isError: false);
    }

    /// <summary>Persists a new pinned set (from the picker) and reloads the gallery.</summary>
    public async Task ApplyPinnedAsync(IEnumerable<string> paths)
    {
        AppSettings.SetQuickLaunch(paths);
        await RefreshAsync();
    }

    public void ShowStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusSeverity = isError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        StatusOpen = !string.IsNullOrEmpty(message);
    }
}

/// <summary>A selectable task row used by the Quick Launch "Add tasks" picker.</summary>
public partial class PinnableTask : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPinned { get; set; }

    // Accessible name for the Quick Launch item container (avoids announcing the class name).
    public override string ToString() => Name;
}
