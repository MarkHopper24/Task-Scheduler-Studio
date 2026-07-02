using CommunityToolkit.Mvvm.ComponentModel;
using Tasker.Core;
using Tasker_App.Services;

namespace Tasker_App.ViewModels;

/// <summary>
/// Backs the guided (non-technical, non-AI) task wizard: holds the chosen recipe, the user's
/// friendly answers, a simple schedule, and turns them into a <see cref="TaskCreateRequest"/>.
/// </summary>
public partial class WizardViewModel : ObservableObject
{
    public IReadOnlyList<TaskRecipe> Recipes => TaskRecipes.All;

    /// <summary>Field answers keyed by <see cref="WizardField.Key"/> (filled by the dialog).</summary>
    public Dictionary<string, string> FieldValues { get; } = new();

    [ObservableProperty]
    public partial TaskRecipe? SelectedRecipe { get; set; }

    // ---- Schedule (friendly) ----
    public IReadOnlyList<string> ScheduleOptions { get; } = new[]
    {
        "Every day", "Every week", "One time", "When I sign in", "When the PC starts",
    };

    [ObservableProperty] public partial int ScheduleIndex { get; set; }
    [ObservableProperty] public partial DateTimeOffset StartDate { get; set; } = DateTimeOffset.Now;
    [ObservableProperty] public partial TimeSpan StartTime { get; set; } = new(9, 0, 0);

    [ObservableProperty] public partial bool Mon { get; set; } = true;
    [ObservableProperty] public partial bool Tue { get; set; }
    [ObservableProperty] public partial bool Wed { get; set; }
    [ObservableProperty] public partial bool Thu { get; set; }
    [ObservableProperty] public partial bool Fri { get; set; }
    [ObservableProperty] public partial bool Sat { get; set; }
    [ObservableProperty] public partial bool Sun { get; set; }

    public bool ShowTime => ScheduleIndex is 0 or 1 or 2;
    public bool ShowWeekdays => ScheduleIndex == 1;
    public bool ShowDate => ScheduleIndex == 2;

    partial void OnScheduleIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowTime));
        OnPropertyChanged(nameof(ShowWeekdays));
        OnPropertyChanged(nameof(ShowDate));
        OnPropertyChanged(nameof(ScheduleSummary));
    }

    [ObservableProperty] public partial string TaskName { get; set; } = string.Empty;
    [ObservableProperty] public partial string Folder { get; set; } = "\\";

    public RecipeResult? Result { get; private set; }

    /// <summary>Builds the recipe result from the current answers (call when entering review).</summary>
    public RecipeResult BuildResult()
    {
        Result = SelectedRecipe!.Build(FieldValues);
        if (string.IsNullOrWhiteSpace(TaskName))
            TaskName = Result.SuggestedName;
        OnPropertyChanged(nameof(ScheduleSummary));
        return Result;
    }

    public string ScheduleSummary => ScheduleIndex switch
    {
        0 => $"Every day at {Time12()}",
        1 => $"Every week on {WeekdaysText()} at {Time12()}",
        2 => $"Once on {StartDate:d} at {Time12()}",
        3 => "Each time you sign in",
        4 => "Each time the PC starts",
        _ => string.Empty,
    };

    public TriggerDto BuildTrigger()
    {
        var start = StartDate.Date + StartTime;
        return ScheduleIndex switch
        {
            0 => new TriggerDto { Kind = TriggerKind.Daily, StartBoundary = start, DaysInterval = 1 },
            1 => BuildWeekly(start),
            2 => new TriggerDto { Kind = TriggerKind.OneTime, StartBoundary = start },
            3 => new TriggerDto { Kind = TriggerKind.AtLogOn },
            4 => new TriggerDto { Kind = TriggerKind.AtStartup },
            _ => new TriggerDto { Kind = TriggerKind.Daily, StartBoundary = start, DaysInterval = 1 },
        };
    }

    private TriggerDto BuildWeekly(DateTime start)
    {
        var dto = new TriggerDto { Kind = TriggerKind.Weekly, StartBoundary = start, WeeksInterval = 1 };
        if (Mon) dto.DaysOfWeek.Add("Monday");
        if (Tue) dto.DaysOfWeek.Add("Tuesday");
        if (Wed) dto.DaysOfWeek.Add("Wednesday");
        if (Thu) dto.DaysOfWeek.Add("Thursday");
        if (Fri) dto.DaysOfWeek.Add("Friday");
        if (Sat) dto.DaysOfWeek.Add("Saturday");
        if (Sun) dto.DaysOfWeek.Add("Sunday");
        if (dto.DaysOfWeek.Count == 0) dto.DaysOfWeek.Add("Monday");
        return dto;
    }

    /// <summary>Builds the final request, or returns null and sets <paramref name="error"/>.</summary>
    public TaskCreateRequest? BuildRequest(out string? error)
    {
        error = null;
        var result = Result ?? BuildResult();

        if (!string.IsNullOrEmpty(result.Validation)) { error = result.Validation; return null; }
        if (string.IsNullOrWhiteSpace(TaskName)) { error = "Give the task a name."; return null; }

        var folder = string.IsNullOrWhiteSpace(Folder) ? "\\" : Folder.Trim();
        if (!folder.StartsWith('\\')) folder = "\\" + folder;

        return new TaskCreateRequest
        {
            Name = TaskName.Trim(),
            Folder = folder,
            Description = result.Description,
            Principal = new PrincipalDto { LogonType = "InteractiveToken" },
            Settings = new SettingsDto { StartWhenAvailable = true },
            Actions = { new ActionDto { Kind = ActionKind.Exec, Command = result.Command, Arguments = result.Arguments, WorkingDirectory = result.WorkingDirectory } },
            Triggers = { BuildTrigger() },
        };
    }

    private string Time12()
    {
        var t = DateTime.Today + StartTime;
        return t.ToString("h:mm tt");
    }

    private string WeekdaysText()
    {
        var days = new List<string>();
        if (Mon) days.Add("Mon");
        if (Tue) days.Add("Tue");
        if (Wed) days.Add("Wed");
        if (Thu) days.Add("Thu");
        if (Fri) days.Add("Fri");
        if (Sat) days.Add("Sat");
        if (Sun) days.Add("Sun");
        return days.Count == 0 ? "Mon" : string.Join(", ", days);
    }
}
