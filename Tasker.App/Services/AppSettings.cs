using Windows.Storage;

namespace Tasker_App.Services;

public enum BackdropChoice { MicaAlt, Mica, Acrylic }
public enum ThemeChoice { System, Light, Dark }
public enum StartPage { Tasks, QuickLaunch, QuickLaunchWidget, Assistant }

/// <summary>Persisted user appearance preferences (per-user, survives restarts).</summary>
public static class AppSettings
{
    private const string BackdropKey = "Backdrop";
    private const string ThemeKey = "Theme";
    private const string FolderPaneKey = "FolderPaneOpen";
    private const string QuickLaunchKey = "QuickLaunch";
    private const string IncludeSubfoldersKey = "IncludeSubfolders";
    private const string OnlyTaskerTasksKey = "OnlyTaskerTasks";
    private const string StartPageKey = "StartPage";
    private const string AiEnabledKey = "AiEnabled";

    private static ApplicationDataContainer Local => ApplicationData.Current.LocalSettings;

    /// <summary>Raised when <see cref="AiEnabled"/> changes, so the shell can show/hide the Assistant.</summary>
    public static event Action<bool>? AiEnabledChanged;

    public static bool FolderPaneOpen
    {
        get => Local.Values[FolderPaneKey] is bool b ? b : true;
        set => Local.Values[FolderPaneKey] = value;
    }

    /// <summary>Whether the Tasks list includes tasks from subfolders. Defaults to on.</summary>
    public static bool IncludeSubfolders
    {
        get => Local.Values[IncludeSubfoldersKey] is bool b ? b : true;
        set => Local.Values[IncludeSubfoldersKey] = value;
    }

    /// <summary>When true, the Tasks list only shows tasks created by Windows Tasker
    /// (RegistrationInfo Source == "Windows Tasker") and hides all other Task Scheduler tasks.</summary>
    public static bool OnlyTaskerTasks
    {
        get => Local.Values[OnlyTaskerTasksKey] is bool b ? b : false;
        set => Local.Values[OnlyTaskerTasksKey] = value;
    }

    public static BackdropChoice Backdrop
    {
        get => Local.Values[BackdropKey] is string s && Enum.TryParse<BackdropChoice>(s, out var v) ? v : BackdropChoice.MicaAlt;
        set => Local.Values[BackdropKey] = value.ToString();
    }

    public static ThemeChoice Theme
    {
        get => Local.Values[ThemeKey] is string s && Enum.TryParse<ThemeChoice>(s, out var v) ? v : ThemeChoice.System;
        set => Local.Values[ThemeKey] = value.ToString();
    }

    /// <summary>Which page the app opens on at launch.</summary>
    public static StartPage StartPage
    {
        get => Local.Values[StartPageKey] is string s && Enum.TryParse<StartPage>(s, out var v) ? v : StartPage.Tasks;
        set => Local.Values[StartPageKey] = value.ToString();
    }

    /// <summary>Whether the in-app AI assistant (GitHub Copilot) is available. When false the
    /// Assistant page is hidden and no Copilot calls are made.</summary>
    public static bool AiEnabled
    {
        get => Local.Values[AiEnabledKey] is bool b ? b : true;
        set
        {
            var changed = AiEnabled != value;
            Local.Values[AiEnabledKey] = value;
            if (changed) AiEnabledChanged?.Invoke(value);
        }
    }

    /// <summary>Task paths pinned to the Quick Launch gallery, in display order. Stored newline-
    /// delimited (task paths never contain newlines).</summary>
    public static IReadOnlyList<string> QuickLaunch
    {
        get => Local.Values[QuickLaunchKey] is string s && s.Length > 0
            ? s.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
        set => Local.Values[QuickLaunchKey] = string.Join('\n', value);
    }

    public static bool IsQuickLaunch(string path) =>
        QuickLaunch.Contains(path, StringComparer.OrdinalIgnoreCase);

    public static void AddQuickLaunch(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var list = QuickLaunch.ToList();
        if (!list.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(path);
            QuickLaunch = list;
        }
    }

    public static void RemoveQuickLaunch(string path) =>
        QuickLaunch = QuickLaunch.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList();

    public static void SetQuickLaunch(IEnumerable<string> paths) =>
        QuickLaunch = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
