using Tasker.Core;

namespace Tasker_App.Services;

/// <summary>
/// UI-facing facade over <see cref="TaskerService"/>. Every call runs on a background thread and
/// returns plain DTOs, so no COM object ever crosses threads and the UI never blocks on the
/// Task Scheduler. A fresh service is created per operation (connecting to Schedule.Service is
/// cheap) which keeps COM apartment usage trivially correct.
/// </summary>
public static class TaskerClient
{
    public static Task<TaskFolderDto> GetFolderTreeAsync() =>
        Run(s => s.GetFolderTree());

    public static Task<List<string>> GetFolderPathsAsync() =>
        Run(s => s.GetFolderPaths());

    public static Task<OperationResult> CreateFolderAsync(string path) =>
        Run(s => s.CreateFolder(path));

    public static Task<OperationResult> DeleteFolderAsync(string path) =>
        Run(s => s.DeleteFolder(path));

    public static Task<List<TaskSummaryDto>> ListTasksAsync(string folder, bool recursive, string? filter = null) =>
        Run(s => s.ListTasks(folder, recursive, filter));

    public static Task<TaskDetailDto?> GetTaskAsync(string path) =>
        Run(s => s.GetTask(path));

    public static Task<string> ExportXmlAsync(string path) =>
        Run(s => s.ExportXml(path));

    public static Task<List<RunningTaskDto>> GetRunningTasksAsync() =>
        Run(s => s.GetRunningTasks());

    public static Task<List<TaskHistoryEntryDto>> GetTaskHistoryAsync(string path, int max = 60) =>
        Run(s => s.GetTaskHistory(path, max));

    public static Task<TaskAnalysisDto> AnalyzeTaskAsync(string path) =>
        Run(s => s.AnalyzeTask(path));

    public static Task<OperationResult> RunTaskAsync(string path) =>
        Run(s => s.RunTask(path));

    public static Task<OperationResult> StopTaskAsync(string path) =>
        Run(s => s.StopTask(path));

    public static Task<OperationResult> SetEnabledAsync(string path, bool enabled) =>
        Run(s => s.SetEnabled(path, enabled));

    public static Task<OperationResult> DeleteTaskAsync(string path) =>
        Run(s => s.DeleteTask(path));

    public static Task<OperationResult> CreateOrUpdateAsync(TaskCreateRequest request) =>
        Run(s => s.CreateOrUpdate(request));

    public static Task<OperationResult> ImportXmlAsync(string folder, string name, string xml) =>
        Run(s => s.ImportXml(folder, name, xml));

    public static Task<(string connectedTo, Version version)> GetConnectionInfoAsync() =>
        Run(s => (s.ConnectedTo, s.HighestSupportedVersion));

    private static Task<T> Run<T>(Func<TaskerService, T> work) => Task.Run(() =>
    {
        using var service = new TaskerService();
        return work(service);
    });
}
