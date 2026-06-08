namespace Tasker.Core;

/// <summary>High-level run state of a task, mirroring the Task Scheduler engine states.</summary>
public enum TaskRunState
{
    Unknown = 0,
    Disabled = 1,
    Queued = 2,
    Ready = 3,
    Running = 4
}

/// <summary>The kinds of triggers the structured editor understands. Anything else round-trips as XML.</summary>
public enum TriggerKind
{
    OneTime,
    Daily,
    Weekly,
    Monthly,
    MonthlyDOW,
    AtStartup,
    AtLogOn,
    OnIdle,
    OnEvent,
    OnSessionStateChange,
    Other
}

/// <summary>The kinds of actions the structured editor understands.</summary>
public enum ActionKind
{
    Exec,
    ComHandler,
    Email,
    ShowMessage,
    Other
}

/// <summary>A node in the Task Scheduler folder tree.</summary>
public sealed class TaskFolderDto
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = "\\";
    public int TaskCount { get; set; }
    public List<TaskFolderDto> Children { get; set; } = new();
}

/// <summary>Lightweight task row used in list views and the MCP <c>list_tasks</c> tool.</summary>
public sealed class TaskSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Folder { get; set; } = "\\";
    public bool Enabled { get; set; }
    public TaskRunState State { get; set; }
    public string StateText => State.ToString();
    public DateTime? LastRunTime { get; set; }
    public DateTime? NextRunTime { get; set; }
    public long LastTaskResult { get; set; }
    public string LastResultText { get; set; } = string.Empty;
    public string TriggersSummary { get; set; } = string.Empty;
    public string ActionsSummary { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>RegistrationInfo &lt;Source&gt;. Tasks created/managed by this app stamp it with
    /// <see cref="TaskerService.AppSource"/> so they can be filtered apart from other tasks.</summary>
    public string Source { get; set; } = string.Empty;
    public bool CreatedByTasker => string.Equals(Source, TaskerService.AppSource, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A single trigger in structured, editable form.</summary>
public sealed class TriggerDto
{
    public TriggerKind Kind { get; set; } = TriggerKind.OneTime;
    public bool Enabled { get; set; } = true;
    public DateTime? StartBoundary { get; set; }
    public DateTime? EndBoundary { get; set; }
    public int DaysInterval { get; set; } = 1;
    public int WeeksInterval { get; set; } = 1;
    public List<string> DaysOfWeek { get; set; } = new();
    public List<int> DaysOfMonth { get; set; } = new();
    public List<string> MonthsOfYear { get; set; } = new();
    public List<string> WeeksOfMonth { get; set; } = new();
    public bool RunOnLastWeek { get; set; }
    public bool RunOnLastDayOfMonth { get; set; }
    public string? StateChange { get; set; }
    public string? UserId { get; set; }
    public string? Delay { get; set; }
    public string? Subscription { get; set; }
    public string? RepetitionInterval { get; set; }
    public string? RepetitionDuration { get; set; }
    public string RawType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

/// <summary>A single action in structured, editable form.</summary>
public sealed class ActionDto
{
    public ActionKind Kind { get; set; } = ActionKind.Exec;
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

/// <summary>Security context the task runs under.</summary>
public sealed class PrincipalDto
{
    public string UserId { get; set; } = string.Empty;
    public string LogonType { get; set; } = "InteractiveToken";
    public bool RunWithHighestPrivileges { get; set; }
}

/// <summary>The behaviour/condition switches exposed in the Settings tab.</summary>
public sealed class SettingsDto
{
    public bool Enabled { get; set; } = true;
    public bool Hidden { get; set; }
    public bool AllowDemandStart { get; set; } = true;
    public bool StartWhenAvailable { get; set; }
    public bool RunOnlyIfNetworkAvailable { get; set; }
    public bool DisallowStartIfOnBatteries { get; set; } = true;
    public bool StopIfGoingOnBatteries { get; set; } = true;
    public bool WakeToRun { get; set; }
    public bool RunOnlyIfIdle { get; set; }
    public bool RestartOnFailure { get; set; }
    public int RestartCount { get; set; }
    public string RestartInterval { get; set; } = "PT1M";
    public string ExecutionTimeLimit { get; set; } = "PT72H";
    public string MultipleInstances { get; set; } = "IgnoreNew";
}

/// <summary>Full editable representation of a task plus read-only runtime info.</summary>
public sealed class TaskDetailDto
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Folder { get; set; } = "\\";
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public TaskRunState State { get; set; }
    public DateTime? LastRunTime { get; set; }
    public DateTime? NextRunTime { get; set; }
    public long LastTaskResult { get; set; }
    public string LastResultText { get; set; } = string.Empty;
    public int MissedRuns { get; set; }
    public PrincipalDto Principal { get; set; } = new();
    public SettingsDto Settings { get; set; } = new();
    public List<TriggerDto> Triggers { get; set; } = new();
    public List<ActionDto> Actions { get; set; } = new();
    public string Xml { get; set; } = string.Empty;
}

/// <summary>A currently-running task instance.</summary>
public sealed class RunningTaskDto
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string CurrentAction { get; set; } = string.Empty;
    public uint EnginePid { get; set; }
    public TaskRunState State { get; set; }
    /// <summary>RegistrationInfo &lt;Source&gt;; "Windows Tasker" for tasks created by this app.</summary>
    public string Source { get; set; } = string.Empty;
    public bool CreatedByTasker => string.Equals(Source, TaskerService.AppSource, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A single entry from the Task Scheduler operational event log for a task.</summary>
public sealed class TaskHistoryEntryDto
{
    public DateTime? TimeCreated { get; set; }
    public int EventId { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string OpCode { get; set; } = string.Empty;
    public string TimeText => TimeCreated is { } t ? t.ToString("g") : string.Empty;
}

/// <summary>Severity of an analysis finding.</summary>
public enum AnalysisSeverity { Info, Notice, Warning }

/// <summary>Overall, evidence-derived risk rating for a task.</summary>
public enum RiskLevel { Low, Medium, High }

/// <summary>A single grounded observation about a task, citing the exact evidence that produced it.</summary>
public sealed class AnalysisFinding
{
    public AnalysisSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>The concrete fact (command, argument, setting, value) the finding is based on.</summary>
    public string Evidence { get; set; } = string.Empty;
    public string SeverityText => Severity.ToString();
}

/// <summary>An evidence-based assessment of one scheduled task, derived only from its definition.</summary>
public sealed class TaskAnalysisDto
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Found { get; set; } = true;
    public RiskLevel Risk { get; set; } = RiskLevel.Low;
    public string RiskText => Risk.ToString();
    public string Summary { get; set; } = string.Empty;
    public List<AnalysisFinding> Findings { get; set; } = new();
    /// <summary>Plain-English recap of the facts the assessment was based on.</summary>
    public List<string> ObservedFacts { get; set; } = new();
}

/// <summary>Request payload for creating or updating a task from structured data.</summary>
public sealed class TaskCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Folder { get; set; } = "\\";
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PrincipalDto Principal { get; set; } = new();
    public SettingsDto Settings { get; set; } = new();
    public List<TriggerDto> Triggers { get; set; } = new();
    public List<ActionDto> Actions { get; set; } = new();
    /// <summary>Optional: when set, the task is created from this raw XML instead of the structured fields.</summary>
    public string? Xml { get; set; }
    /// <summary>Optional user account to register under (for stored-credential tasks).</summary>
    public string? UserId { get; set; }
    public string? Password { get; set; }
}

/// <summary>Result of a mutating operation, suitable for both UI feedback and MCP responses.</summary>
public sealed class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Path { get; set; }

    public static OperationResult Ok(string message, string? path = null) =>
        new() { Success = true, Message = message, Path = path };

    public static OperationResult Fail(string message) =>
        new() { Success = false, Message = message };
}
