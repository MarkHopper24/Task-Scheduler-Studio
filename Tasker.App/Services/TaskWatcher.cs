using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Tasker_App.Services;

/// <summary>
/// Watches a task the user started from the app and raises a Windows toast when it finishes,
/// reporting the last result. Polling is bounded so watchers never run indefinitely.
/// </summary>
public static class TaskWatcher
{
    private static bool _registered;
    private static readonly HashSet<string> _watching = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    public static void EnsureRegistered()
    {
        if (_registered) return;
        try { AppNotificationManager.Default.Register(); _registered = true; }
        catch { /* notifications unavailable */ }
    }

    public static void Watch(string path, string name)
    {
        lock (_lock)
        {
            if (!_watching.Add(path)) return; // already watching
        }
        _ = Task.Run(() => WatchLoopAsync(path, name));
    }

    private static async Task WatchLoopAsync(string path, string name)
    {
        try
        {
            var everRunning = false;
            var start = DateTime.UtcNow;
            var maxWatch = TimeSpan.FromMinutes(30);

            while (DateTime.UtcNow - start < maxWatch)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                var running = await TaskerClient.GetRunningTasksAsync();
                var isRunning = running.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

                if (isRunning) { everRunning = true; continue; }

                // Finished after running, or never appeared running after a short grace (quick task).
                if (everRunning || DateTime.UtcNow - start > TimeSpan.FromSeconds(20))
                {
                    var detail = await TaskerClient.GetTaskAsync(path);
                    Notify(name, detail?.LastResultText ?? "Completed");
                    break;
                }
            }
        }
        catch { /* best-effort */ }
        finally
        {
            lock (_lock) { _watching.Remove(path); }
        }
    }

    private static void Notify(string name, string result)
    {
        try
        {
            EnsureRegistered();
            var toast = new AppNotificationBuilder()
                .AddText("Scheduled task finished")
                .AddText($"{name} \u2014 {result}")
                .BuildNotification();
            AppNotificationManager.Default.Show(toast);
        }
        catch { /* ignore */ }
    }
}
