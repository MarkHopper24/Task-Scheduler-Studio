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

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TaskSummaryDto? SelectedTask { get; set; }

    public bool IsEmpty => Upcoming.Count == 0 && !IsBusy;

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
            var upcoming = all
                .Where(t => t.NextRunTime is { } n && n > now)
                .Where(t => !onlyTasker || t.CreatedByTasker)
                .OrderBy(t => t.NextRunTime)
                .Take(200)
                .ToList();

            Upcoming.Clear();
            foreach (var t in upcoming) Upcoming.Add(t);
            StatusMessage = $"{Upcoming.Count} upcoming run(s).";
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
}
