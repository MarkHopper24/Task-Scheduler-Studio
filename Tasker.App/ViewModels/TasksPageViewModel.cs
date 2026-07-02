using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tasker.Core;
using Tasker_App.Services;

namespace Tasker_App.ViewModels;

/// <summary>
/// Drives the Tasks browser: the folder tree, the filtered task table for the selected folder,
/// the detail pane for the selected task, and all per-task operations.
/// </summary>
public partial class TasksPageViewModel : ObservableObject
{
    public ObservableCollection<FolderNode> Folders { get; } = new();
    public ObservableCollection<TaskSummaryDto> Tasks { get; } = new();

    [ObservableProperty]
    public partial FolderNode? SelectedFolder { get; set; }

    [ObservableProperty]
    public partial TaskSummaryDto? SelectedTask { get; set; }

    [ObservableProperty]
    public partial TaskDetailDto? Detail { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; } = Services.AppSettings.IncludeSubfolders;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string CurrentFolderPath { get; set; } = "\\";

    [ObservableProperty]
    public partial bool IsFolderPaneOpen { get; set; } = Services.AppSettings.FolderPaneOpen;

    public bool IsFolderPaneCollapsed => !IsFolderPaneOpen;

    partial void OnIsFolderPaneOpenChanged(bool value)
    {
        Services.AppSettings.FolderPaneOpen = value;
        OnPropertyChanged(nameof(IsFolderPaneCollapsed));
    }

    [RelayCommand]
    private void ToggleFolderPane() => IsFolderPaneOpen = !IsFolderPaneOpen;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    public bool HasDetail => Detail is not null;
    public bool HasSelection => SelectedTask is not null;
    public bool IsNotElevated => !Helpers.Elevation.IsElevated;
    public bool CanToggleEnabled => Detail is not null;
    public string EnableToggleLabel => Detail?.Enabled == true ? "Disable" : "Enable";
    public string EnableToggleGlyph => Detail?.Enabled == true ? "\uE711" : "\uE73E";

    private List<TaskSummaryDto> _allTasks = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher =
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    public TasksPageViewModel()
    {
        Services.AppEvents.TasksChanged += OnTasksChangedExternally;
    }

    private void OnTasksChangedExternally()
    {
        _dispatcher.TryEnqueue(async () => await RefreshAsync());
    }

    public async Task InitializeAsync()
    {
        await LoadFoldersAsync();
        await LoadTasksAsync();
    }

    public async Task LoadFoldersAsync()
    {
        try
        {
            var root = await TaskerClient.GetFolderTreeAsync();

            HashSet<string>? visible = null;
            Dictionary<string, int>? taskerCounts = null;
            // Global "only WinTask Scheduler tasks" filter: also hide folders whose subtree
            // contains no Tasker-created tasks.
            if (Services.AppSettings.OnlyTaskerTasks)
            {
                var all = await TaskerClient.ListTasksAsync("\\", recursive: true);
                visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "\\" };
                taskerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in all.Where(t => t.CreatedByTasker))
                {
                    var folder = string.IsNullOrEmpty(t.Folder) ? "\\" : t.Folder;
                    taskerCounts[folder] = taskerCounts.TryGetValue(folder, out var c) ? c + 1 : 1;
                    AddWithAncestors(visible, folder);
                }
            }

            Folders.Clear();
            Folders.Add(new FolderNode(root, expand: true, visiblePaths: visible, taskerCounts: taskerCounts));
            SelectedFolder ??= Folders[0];
        }
        catch (Exception ex)
        {
            ShowError($"Could not load folders: {ex.Message}");
        }
    }

    /// <summary>Adds <paramref name="folder"/> and all of its ancestor folder paths to <paramref name="set"/>,
    /// so intermediate folders that only contain Tasker tasks in descendants stay visible.</summary>
    private static void AddWithAncestors(HashSet<string> set, string folder)
    {
        var p = folder;
        while (!string.IsNullOrEmpty(p))
        {
            set.Add(p);
            if (p == "\\") break;
            var idx = p.LastIndexOf('\\');
            p = idx <= 0 ? "\\" : p.Substring(0, idx);
        }
    }

    public async Task LoadTasksAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await ReloadTasksAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Reloads the task table for the current folder. Unlike <see cref="LoadTasksAsync"/>
    /// this does not touch <see cref="IsBusy"/>, so it can be awaited from operations that already
    /// hold the busy flag (run/stop/enable/disable/delete/bulk) — otherwise the self-guard on
    /// <see cref="LoadTasksAsync"/> would skip the refresh and leave the list stale.</summary>
    private async Task ReloadTasksAsync()
    {
        HasError = false;
        try
        {
            var folder = SelectedFolder?.Path ?? "\\";
            CurrentFolderPath = folder;
            _allTasks = await TaskerClient.ListTasksAsync(folder, IncludeSubfolders);
            ApplyFilter();
            StatusMessage = $"{Tasks.Count} task(s) in {folder}";
        }
        catch (Exception ex)
        {
            ShowError($"Could not load tasks: {ex.Message}");
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<TaskSummaryDto> view = _allTasks;

        // Global preference (Settings): hide everything not created by WinTask Scheduler.
        if (Services.AppSettings.OnlyTaskerTasks)
            view = view.Where(t => t.CreatedByTasker);

        var q = SearchText?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            view = view.Where(t =>
                t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.ActionsSummary.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.TriggersSummary.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Author.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        view = SortColumn switch
        {
            "Status" => Order(view, t => t.StateText),
            "NextRun" => Order(view, t => t.NextRunTime ?? DateTime.MaxValue),
            "LastRun" => Order(view, t => t.LastRunTime ?? DateTime.MaxValue),
            _ => Order(view, t => t.Name),
        };

        Tasks.Clear();
        foreach (var t in view) Tasks.Add(t);
    }

    private IEnumerable<TaskSummaryDto> Order<TKey>(IEnumerable<TaskSummaryDto> src, Func<TaskSummaryDto, TKey> key) =>
        SortAscending ? src.OrderBy(key) : src.OrderByDescending(key);

    [ObservableProperty]
    public partial string SortColumn { get; set; } = "Name";

    [ObservableProperty]
    public partial bool SortAscending { get; set; } = true;

    public string HeaderName => "Name" + Arrow("Name");
    public string HeaderStatus => "Status" + Arrow("Status");
    public string HeaderNextRun => "Next run" + Arrow("NextRun");
    public string HeaderLastRun => "Last run" + Arrow("LastRun");

    private string Arrow(string col) =>
        col == SortColumn ? (SortAscending ? "  \u2191" : "  \u2193") : string.Empty;

    [RelayCommand]
    private void Sort(string column)
    {
        if (SortColumn == column) SortAscending = !SortAscending;
        else { SortColumn = column; SortAscending = true; }
        ApplyFilter();
        foreach (var p in new[] { nameof(HeaderName), nameof(HeaderStatus), nameof(HeaderNextRun), nameof(HeaderLastRun) })
            OnPropertyChanged(p);
    }

    // ---- Multi-selection / bulk actions ----
    private readonly List<string> _selectedPaths = new();

    [ObservableProperty]
    public partial int SelectedCount { get; set; }

    public bool HasMultiSelection => SelectedCount > 1;

    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(HasMultiSelection));

    public void SetSelection(IEnumerable<TaskSummaryDto> selected)
    {
        _selectedPaths.Clear();
        foreach (var t in selected) _selectedPaths.Add(t.Path);
        SelectedCount = _selectedPaths.Count;
    }

    public IReadOnlyList<string> SelectedPaths => _selectedPaths;

    [RelayCommand]
    private Task RunSelectedAsync() => BulkAsync(p => TaskerClient.RunTaskAsync(p), "Started");

    [RelayCommand]
    private Task EnableSelectedAsync() => BulkAsync(p => TaskerClient.SetEnabledAsync(p, true), "Enabled");

    [RelayCommand]
    private Task DisableSelectedAsync() => BulkAsync(p => TaskerClient.SetEnabledAsync(p, false), "Disabled");

    public async Task BulkAsync(Func<string, Task<OperationResult>> op, string verb)
    {
        var paths = _selectedPaths.ToList();
        if (paths.Count == 0) return;
        IsBusy = true;
        try
        {
            var ok = 0;
            foreach (var p in paths)
            {
                var r = await op(p);
                if (r.Success) ok++;
            }
            ShowInfo($"{verb} {ok} of {paths.Count} task(s).");
            await ReloadTasksAsync();
        }
        finally { IsBusy = false; }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    async partial void OnSelectedFolderChanged(FolderNode? value)
    {
        SelectedTask = null;
        Detail = null;
        await LoadTasksAsync();
    }

    async partial void OnIncludeSubfoldersChanged(bool value)
    {
        Services.AppSettings.IncludeSubfolders = value;
        await LoadTasksAsync();
    }

    async partial void OnSelectedTaskChanged(TaskSummaryDto? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        History.Clear();
        HistoryStatus = string.Empty;
        if (value is null)
        {
            Detail = null;
            return;
        }
        await LoadDetailAsync(value.Path);
    }

    partial void OnDetailChanged(TaskDetailDto? value)
    {
        OnPropertyChanged(nameof(HasDetail));
        OnPropertyChanged(nameof(CanToggleEnabled));
        OnPropertyChanged(nameof(EnableToggleLabel));
        OnPropertyChanged(nameof(EnableToggleGlyph));
    }

    private async Task LoadDetailAsync(string path)
    {
        try { Detail = await TaskerClient.GetTaskAsync(path); }
        catch (Exception ex) { ShowError($"Could not load task: {ex.Message}"); }
    }

    // ---- Run history (lazy-loaded when the History expander opens) ----
    public ObservableCollection<TaskHistoryEntryDto> History { get; } = new();

    [ObservableProperty]
    public partial bool HistoryLoading { get; set; }

    [ObservableProperty]
    public partial string HistoryStatus { get; set; } = string.Empty;

    public async Task LoadHistoryAsync()
    {
        var path = SelectedTask?.Path;
        if (string.IsNullOrEmpty(path)) return;
        HistoryLoading = true;
        try
        {
            var entries = await TaskerClient.GetTaskHistoryAsync(path);
            History.Clear();
            foreach (var e in entries) History.Add(e);
            HistoryStatus = History.Count == 0
                ? "No history found. Enable \u201CAll Tasks History\u201D in Task Scheduler to record events."
                : $"{History.Count} recent event(s).";
        }
        catch (Exception ex) { HistoryStatus = ex.Message; }
        finally { HistoryLoading = false; }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadFoldersAsync();
        await LoadTasksAsync();
        if (SelectedTask is not null) await LoadDetailAsync(SelectedTask.Path);
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (SelectedTask is null) return;
        var path = SelectedTask.Path;
        var name = SelectedTask.Name;
        await ApplyOp(() => TaskerClient.RunTaskAsync(path));
        Services.TaskWatcher.Watch(path, name);
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (SelectedTask is null) return;
        await ApplyOp(() => TaskerClient.StopTaskAsync(SelectedTask.Path));
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync()
    {
        if (Detail is null || SelectedTask is null) return;
        await ApplyOp(() => TaskerClient.SetEnabledAsync(SelectedTask.Path, !Detail.Enabled));
    }

    private async Task ApplyOp(Func<Task<OperationResult>> op)
    {
        var path = SelectedTask?.Path;
        IsBusy = true;
        try
        {
            var result = await op();
            if (result.Success) ShowInfo(result.Message);
            else ShowError(result.Message);
            await ReloadTasksAsync();
            ReselectByPath(path);
            if (path is not null) await LoadDetailAsync(path);
        }
        finally { IsBusy = false; }
    }

    private void ReselectByPath(string? path)
    {
        if (path is null) return;
        SelectedTask = Tasks.FirstOrDefault(t => t.Path == path);
    }

    public void ShowError(string message)
    {
        HasError = true;
        StatusMessage = message;
        IsStatusOpen = true;
    }

    public void ShowInfo(string message)
    {
        HasError = false;
        StatusMessage = message;
        IsStatusOpen = true;
    }
}
