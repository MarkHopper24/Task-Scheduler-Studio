namespace Tasker_App.Services;

/// <summary>Lightweight app-wide signals so pages can react to changes made elsewhere
/// (e.g., the Assistant creating a task should refresh the Tasks browser).</summary>
public static class AppEvents
{
    public static event Action? TasksChanged;

    public static void RaiseTasksChanged() => TasksChanged?.Invoke();
}
