using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tasker.Core;
using Tasker_App.Services;

namespace Tasker_App.ViewModels;

/// <summary>Lists all enabled tasks with a future next-run time, soonest first.</summary>
public partial class UpcomingPageViewModel : ObservableObject
{
    public ObservableCollection<TaskSummaryDto> Upcoming { get; } = new();

    public const string AllProcesses = "All processes";
    public ObservableCollection<string> AvailableProcesses { get; } = new() { AllProcesses };

    [ObservableProperty]
    public partial string ProcessFilter { get; set; } = AllProcesses;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TaskSummaryDto? SelectedTask { get; set; }

    public bool IsEmpty => Upcoming.Count == 0 && !IsBusy;

    private List<TaskSummaryDto> _allUpcoming = new();

    partial void OnProcessFilterChanged(string value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task RunSelectedAsync()
    {
        if (SelectedTask is null) return;
        var result = await TaskerClient.RunTaskAsync(SelectedTask.Path);
        StatusMessage = result.Success ? $"Started \u201C{SelectedTask.Name}\u201D." : result.Message;
    }

    [RelayCommand]
    private async Task DisableSelectedAsync()
    {
        if (SelectedTask is null) return;
        var result = await TaskerClient.SetEnabledAsync(SelectedTask.Path, false);
        StatusMessage = result.Message;
        await RefreshAsync(); // a disabled task no longer has an upcoming run
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var all = await TaskerClient.ListTasksAsync("\\", recursive: true);
            var onlyTasker = Services.AppSettings.OnlyTaskerTasks;
            var now = DateTime.Now;
            _allUpcoming = all
                .Where(t => t.NextRunTime is { } n && n > now)
                .Where(t => !onlyTasker || t.CreatedByTasker)
                .OrderBy(t => t.NextRunTime)
                .Take(200)
                .ToList();

            RebuildAvailableProcesses();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<TaskSummaryDto> view = _allUpcoming;

        if (!string.IsNullOrEmpty(ProcessFilter) && ProcessFilter != AllProcesses)
            view = view.Where(t => string.Equals(t.ProcessName, ProcessFilter, StringComparison.OrdinalIgnoreCase));

        var q = SearchText?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            view = view.Where(t =>
                t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.TriggersSummary.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.ActionsSummary.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        Upcoming.Clear();
        foreach (var t in view) Upcoming.Add(t);
        StatusMessage = $"{Upcoming.Count} upcoming run(s).";
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void RebuildAvailableProcesses()
    {
        var current = ProcessFilter;
        var names = _allUpcoming
            .Select(t => t.ProcessName)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

        AvailableProcesses.Clear();
        AvailableProcesses.Add(AllProcesses);
        foreach (var n in names) AvailableProcesses.Add(n);

        ProcessFilter = AvailableProcesses.Contains(current) ? current : AllProcesses;
    }
}
