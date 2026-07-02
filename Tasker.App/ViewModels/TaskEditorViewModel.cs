using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tasker.Core;

namespace Tasker_App.ViewModels;

public sealed record NamedValue(string Label, string Value);

/// <summary>
/// Coordinates the create/edit experience: general info, conditions/settings, triggers, actions,
/// and the raw-XML escape hatch. Produces a <see cref="TaskCreateRequest"/> for the service.
/// </summary>
public partial class TaskEditorViewModel : ObservableObject
{
    public bool IsEditMode { get; }
    public string Title => IsEditMode ? "Edit task" : "Create task";
    private readonly string? _originalPath;

    /// <summary>When editing, the task's original path. If the user changes the name or folder the
    /// save is a move: the caller registers the task at its new path and deletes this original.</summary>
    public string? OriginalPath => _originalPath;

    public ObservableCollection<TriggerEditViewModel> Triggers { get; } = new();
    public ObservableCollection<ActionEditViewModel> Actions { get; } = new();

    /// <summary>Existing folder paths, offered as suggestions in the folder picker.</summary>
    public ObservableCollection<string> AvailableFolders { get; } = new();

    public void SetFolders(IEnumerable<string> folders)
    {
        AvailableFolders.Clear();
        foreach (var f in folders) AvailableFolders.Add(f);
    }

    public IReadOnlyList<NamedValue> TimeLimitOptions { get; } = new[]
    {
        new NamedValue("1 hour", "PT1H"),
        new NamedValue("8 hours", "PT8H"),
        new NamedValue("1 day", "P1D"),
        new NamedValue("3 days", "P3D"),
        new NamedValue("No limit", ""),
    };

    public IReadOnlyList<NamedValue> InstancesOptions { get; } = new[]
    {
        new NamedValue("Do not start a new instance", "IgnoreNew"),
        new NamedValue("Run a new instance in parallel", "Parallel"),
        new NamedValue("Queue a new instance", "Queue"),
        new NamedValue("Stop the existing instance", "StopExisting"),
    };

    public IReadOnlyList<NamedValue> LogonOptions { get; } = new[]
    {
        new NamedValue("Run only when the user is logged on", "InteractiveToken"),
        new NamedValue("Run whether the user is logged on or not (store password)", "Password"),
        new NamedValue("Run whether logged on or not, don't store password (S4U)", "S4U"),
        new NamedValue("Run as SYSTEM (no password)", "ServiceAccount"),
    };

    [ObservableProperty]
    public partial NamedValue SelectedLogon { get; set; }

    [ObservableProperty]
    public partial string RunAsUser { get; set; } = Environment.UserName;

    public string RunAsPassword { get; set; } = string.Empty;

    public bool ShowRunAsPassword => SelectedLogon?.Value == "Password";

    partial void OnSelectedLogonChanged(NamedValue value) => OnPropertyChanged(nameof(ShowRunAsPassword));

    [ObservableProperty]
    public partial string TaskName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Folder { get; set; } = "\\";

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Author { get; set; } = Environment.UserName;

    [ObservableProperty]
    public partial bool RunWithHighestPrivileges { get; set; }

    [ObservableProperty]
    public partial bool Hidden { get; set; }

    [ObservableProperty]
    public partial bool TaskEnabled { get; set; } = true;

    // Conditions / settings
    [ObservableProperty]
    public partial bool AllowDemandStart { get; set; } = true;
    [ObservableProperty]
    public partial bool StartWhenAvailable { get; set; }
    [ObservableProperty]
    public partial bool RunOnlyIfNetworkAvailable { get; set; }
    [ObservableProperty]
    public partial bool DisallowStartIfOnBatteries { get; set; } = true;
    [ObservableProperty]
    public partial bool StopIfGoingOnBatteries { get; set; } = true;
    [ObservableProperty]
    public partial bool WakeToRun { get; set; }
    [ObservableProperty]
    public partial bool RunOnlyIfIdle { get; set; }
    [ObservableProperty]
    public partial bool RestartOnFailure { get; set; }
    [ObservableProperty]
    public partial int RestartCount { get; set; } = 3;

    [ObservableProperty]
    public partial NamedValue SelectedTimeLimit { get; set; }

    [ObservableProperty]
    public partial NamedValue SelectedInstances { get; set; }

    // Raw XML escape hatch
    [ObservableProperty]
    public partial bool UseRawXml { get; set; }

    [ObservableProperty]
    public partial string Xml { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ValidationMessage { get; set; } = string.Empty;

    public TaskEditorViewModel(TaskDetailDto? existing = null, bool asDuplicate = false)
    {
        SelectedTimeLimit = TimeLimitOptions[3];
        SelectedInstances = InstancesOptions[0];
        SelectedLogon = LogonOptions[0];

        if (existing is null)
        {
            Triggers.Add(new TriggerEditViewModel());
            Actions.Add(new ActionEditViewModel());
            return;
        }

        if (!asDuplicate)
        {
            IsEditMode = true;
            _originalPath = existing.Path;
        }
        TaskName = asDuplicate ? $"{existing.Name} (Copy)" : existing.Name;
        Folder = existing.Folder;
        Description = existing.Description;
        Author = string.IsNullOrEmpty(existing.Author) ? Environment.UserName : existing.Author;
        RunWithHighestPrivileges = existing.Principal.RunWithHighestPrivileges;
        RunAsUser = string.IsNullOrEmpty(existing.Principal.UserId) ? Environment.UserName : existing.Principal.UserId;
        SelectedLogon = LogonOptions.FirstOrDefault(o => o.Value == existing.Principal.LogonType) ?? LogonOptions[0];
        Hidden = existing.Settings.Hidden;
        TaskEnabled = existing.Enabled;
        AllowDemandStart = existing.Settings.AllowDemandStart;
        StartWhenAvailable = existing.Settings.StartWhenAvailable;
        RunOnlyIfNetworkAvailable = existing.Settings.RunOnlyIfNetworkAvailable;
        DisallowStartIfOnBatteries = existing.Settings.DisallowStartIfOnBatteries;
        StopIfGoingOnBatteries = existing.Settings.StopIfGoingOnBatteries;
        WakeToRun = existing.Settings.WakeToRun;
        RunOnlyIfIdle = existing.Settings.RunOnlyIfIdle;
        RestartOnFailure = existing.Settings.RestartOnFailure;
        RestartCount = existing.Settings.RestartCount > 0 ? existing.Settings.RestartCount : 3;
        SelectedTimeLimit = TimeLimitOptions.FirstOrDefault(o => o.Value == existing.Settings.ExecutionTimeLimit) ?? TimeLimitOptions[3];
        SelectedInstances = InstancesOptions.FirstOrDefault(o => o.Value == existing.Settings.MultipleInstances) ?? InstancesOptions[0];
        Xml = existing.Xml;

        foreach (var tr in existing.Triggers)
            Triggers.Add(TriggerEditViewModel.FromDto(tr));
        foreach (var ac in existing.Actions.Where(a => a.Kind == ActionKind.Exec))
            Actions.Add(ActionEditViewModel.FromDto(ac));

        // Triggers/actions the structured editor can't represent are preserved only via raw XML.
        var unsupported = existing.Triggers.Any(t => !TriggerEditViewModel.Kinds.Contains(t.Kind))
                          || existing.Actions.Any(a => a.Kind != ActionKind.Exec);
        if (unsupported || Triggers.Count == 0)
            UseRawXml = true;
        if (Actions.Count == 0)
            Actions.Add(new ActionEditViewModel());
    }

    [RelayCommand]
    private void AddTrigger() => Triggers.Add(new TriggerEditViewModel());

    [RelayCommand]
    private void RemoveTrigger(TriggerEditViewModel trigger) => Triggers.Remove(trigger);

    [RelayCommand]
    private void AddAction() => Actions.Add(new ActionEditViewModel());

    [RelayCommand]
    private void RemoveAction(ActionEditViewModel action) => Actions.Remove(action);

    /// <summary>Validates input and builds the request, or returns null and sets <see cref="ValidationMessage"/>.</summary>
    public TaskCreateRequest? BuildRequest()
    {
        ValidationMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(TaskName))
        {
            ValidationMessage = "Enter a task name.";
            return null;
        }

        var folder = string.IsNullOrWhiteSpace(Folder) ? "\\" : Folder.Trim();
        if (!folder.StartsWith('\\')) folder = "\\" + folder;

        var req = new TaskCreateRequest
        {
            Name = TaskName.Trim(),
            Folder = folder,
            Description = Description,
            Author = Author,
        };

        if (UseRawXml)
        {
            if (string.IsNullOrWhiteSpace(Xml))
            {
                ValidationMessage = "Raw XML is empty.";
                return null;
            }
            req.Xml = Xml;
            return req;
        }

        var actions = Actions.Where(a => !string.IsNullOrWhiteSpace(a.Command)).Select(a => a.ToDto()).ToList();
        if (actions.Count == 0)
        {
            ValidationMessage = "Add at least one action with a command.";
            return null;
        }

        // Validate schedule triggers so a half-filled trigger can't silently fall back to a much
        // broader schedule in the engine (e.g. weekly with no days becomes "every day").
        foreach (var t in Triggers)
        {
            if (t.Kind is TriggerKind.Weekly or TriggerKind.MonthlyDOW && !t.AnyWeekdaySelected)
            {
                ValidationMessage = "Select at least one day of the week for the weekly / monthly-by-weekday trigger.";
                return null;
            }
            if (t.Kind == TriggerKind.MonthlyDOW && !t.AnyWeekSelected)
            {
                ValidationMessage = "Select at least one week (first, second, \u2026 or last) for the monthly-by-weekday trigger.";
                return null;
            }
            if (t.Kind == TriggerKind.Monthly && !t.AnyMonthDaySelected)
            {
                ValidationMessage = "Enter at least one day of the month (1\u201331), or choose the last day, for the monthly trigger.";
                return null;
            }
            if (t.Kind is TriggerKind.Monthly or TriggerKind.MonthlyDOW && !t.AnyMonthSelected)
            {
                ValidationMessage = "Select at least one month for the monthly trigger.";
                return null;
            }
        }

        var logon = SelectedLogon?.Value ?? "InteractiveToken";
        req.Principal = new PrincipalDto
        {
            RunWithHighestPrivileges = RunWithHighestPrivileges,
            LogonType = logon,
        };
        if (logon == "ServiceAccount")
        {
            // The UI presents this option as "Run as SYSTEM". Honor an explicitly-typed built-in
            // service account, but never register a normal user account (e.g. the pre-filled current
            // user) under ServiceAccount logon — fall back to SYSTEM so the task is valid.
            req.Principal.UserId = ResolveServiceAccount(RunAsUser);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(RunAsUser)) req.Principal.UserId = RunAsUser.Trim();
            if (logon == "Password")
            {
                if (string.IsNullOrEmpty(RunAsPassword))
                {
                    ValidationMessage = "Enter the account password, or choose a different logon option.";
                    return null;
                }
                req.UserId = RunAsUser.Trim();
                req.Password = RunAsPassword;
            }
        }
        req.Settings = new SettingsDto
        {
            Enabled = TaskEnabled,
            Hidden = Hidden,
            AllowDemandStart = AllowDemandStart,
            StartWhenAvailable = StartWhenAvailable,
            RunOnlyIfNetworkAvailable = RunOnlyIfNetworkAvailable,
            DisallowStartIfOnBatteries = DisallowStartIfOnBatteries,
            StopIfGoingOnBatteries = StopIfGoingOnBatteries,
            WakeToRun = WakeToRun,
            RunOnlyIfIdle = RunOnlyIfIdle,
            RestartOnFailure = RestartOnFailure,
            RestartCount = RestartOnFailure ? RestartCount : 0,
            ExecutionTimeLimit = SelectedTimeLimit.Value,
            MultipleInstances = SelectedInstances.Value,
        };
        req.Triggers = Triggers.Select(t => t.ToDto()).ToList();
        req.Actions = actions;
        return req;
    }

    /// <summary>Maps the "Run as user" text to a valid built-in service account for ServiceAccount
    /// logon. Recognized system accounts are honored; anything else (including the pre-filled current
    /// user) falls back to SYSTEM, which is what the "Run as SYSTEM" option promises.</summary>
    private static string ResolveServiceAccount(string? user)
    {
        var u = user?.Trim() ?? string.Empty;
        if (u.Length == 0) return "SYSTEM";
        string[] known = { "SYSTEM", "LOCALSYSTEM", "LOCAL SERVICE", "NETWORK SERVICE" };
        var leaf = u.Contains('\\') ? u[(u.LastIndexOf('\\') + 1)..] : u;
        return known.Any(k => leaf.Equals(k, StringComparison.OrdinalIgnoreCase)) ? leaf : "SYSTEM";
    }
}
