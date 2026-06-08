namespace Tasker_App.Services;

/// <summary>
/// Coordinates the Quick Launch "minimal view" (floating widget) state between the page that
/// toggles it and the shell (which hides the navigation pane and resizes/pins the window).
/// </summary>
public static class WidgetMode
{
    public static event Action<bool>? Changed;

    public static bool IsActive { get; private set; }

    public static void Set(bool active)
    {
        if (IsActive == active) return;
        IsActive = active;
        Changed?.Invoke(active);
    }
}
