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
    public partial RunningTaskDto? SelectedTask { get; set; }

    public bool IsEmpty => Running.Count == 0 && !IsBusy;
    public bool HasSelection => SelectedTask is not null;

    private List<RunningTaskDto> _allRunning = new();

    partial void OnSelectedTaskChanged(RunningTaskDto? value) => OnPropertyChanged(nameof(HasSelection));

    partial void OnProcessFilterChanged(string value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

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
            _allRunning = list;
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
        IEnumerable<RunningTaskDto> view = _allRunning;

        if (!string.IsNullOrEmpty(ProcessFilter) && ProcessFilter != AllProcesses)
            view = view.Where(r => string.Equals(r.ProcessName, ProcessFilter, StringComparison.OrdinalIgnoreCase));

        var q = SearchText?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            view = view.Where(r =>
                r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Path.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.CurrentAction.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        Running.Clear();
        foreach (var r in view) Running.Add(r);
        StatusMessage = $"{Running.Count} task(s) running";
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void RebuildAvailableProcesses()
    {
        var current = ProcessFilter;
        var names = _allRunning
            .Select(r => r.ProcessName)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

        AvailableProcesses.Clear();
        AvailableProcesses.Add(AllProcesses);
        foreach (var n in names) AvailableProcesses.Add(n);

        ProcessFilter = AvailableProcesses.Contains(current) ? current : AllProcesses;
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
