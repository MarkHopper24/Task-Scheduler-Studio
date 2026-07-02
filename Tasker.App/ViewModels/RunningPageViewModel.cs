using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tasker.Core;
using Tasker_App.Services;

namespace Tasker_App.ViewModels;

/// <summary>Lists currently-running task instances, with manual and timed refresh.</summary>
public partial class RunningPageViewModel : ObservableObject
{
    public ObservableCollection<RunningTaskDto> Running { get; } = new();

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial RunningTaskDto? SelectedTask { get; set; }

    public bool IsEmpty => Running.Count == 0 && !IsBusy;
    public bool HasSelection => SelectedTask is not null;

    partial void OnSelectedTaskChanged(RunningTaskDto? value) => OnPropertyChanged(nameof(HasSelection));

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var list = await TaskerClient.GetRunningTasksAsync();
            if (Services.AppSettings.OnlyTaskerTasks)
                list = list.Where(r => r.CreatedByTasker).ToList();
            Running.Clear();
            foreach (var r in list) Running.Add(r);
            StatusMessage = $"{Running.Count} task(s) running";
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

    [RelayCommand]
    private async Task StopAsync()
    {
        if (SelectedTask is null) return;
        var result = await TaskerClient.StopTaskAsync(SelectedTask.Path);
        StatusMessage = result.Message;
        await RefreshAsync();
    }
}
