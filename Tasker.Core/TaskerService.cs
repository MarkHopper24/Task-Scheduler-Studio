using System.Xml;
using Microsoft.Win32.TaskScheduler;

namespace Tasker.Core;

/// <summary>
/// Single source of truth for both the WinUI app and the MCP server. Wraps the Task Scheduler
/// V2 COM API (<c>Schedule.Service</c>) via the Microsoft.Win32.TaskScheduler managed library,
/// so every task it creates lives in the SAME store as the built-in Windows Task Scheduler and
/// is visible/editable from both. All operations are synchronous COM calls; callers that need
/// responsiveness should marshal to a background thread.
/// </summary>
public sealed class TaskerService : IDisposable
{
    /// <summary>Stamped into each created/updated task's RegistrationInfo &lt;Source&gt; so tasks
    /// originating from this app (UI or MCP) can be told apart from other Task Scheduler tasks.</summary>
    public const string AppSource = "Windows Tasker";

    private readonly TaskService _ts;

    public TaskerService()
    {
        _ts = new TaskService();
    }

    public string ConnectedTo => _ts.TargetServer ?? Environment.MachineName;
    public Version HighestSupportedVersion => _ts.HighestSupportedVersion;

    // ---------------------------------------------------------------- Folders

    /// <summary>Returns the full folder tree rooted at <c>\</c>.</summary>
    public TaskFolderDto GetFolderTree()
    {
        return MapFolder(_ts.RootFolder);
    }

    private static TaskFolderDto MapFolder(TaskFolder folder)
    {
        var dto = new TaskFolderDto
        {
            Name = string.IsNullOrEmpty(folder.Name) ? "\\" : folder.Name,
            Path = folder.Path.Length == 0 ? "\\" : folder.Path,
        };
        try { dto.TaskCount = folder.GetTasks().Count; } catch { dto.TaskCount = 0; }
        try
        {
            foreach (var sub in folder.SubFolders)
                dto.Children.Add(MapFolder(sub));
        }
        catch { /* access denied on some system subfolders */ }
        return dto;
    }

    /// <summary>Returns every folder path in the tree, depth-first, starting with the root.</summary>
    public List<string> GetFolderPaths()
    {
        var paths = new List<string>();
        Flatten(GetFolderTree(), paths);
        return paths;
    }

    private static void Flatten(TaskFolderDto node, List<string> sink)
    {
        sink.Add(node.Path);
        foreach (var c in node.Children) Flatten(c, sink);
    }

    /// <summary>Creates a folder (and any missing parents).</summary>
    public OperationResult CreateFolder(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path == "\\")
                return OperationResult.Fail("Folder name is required.");
            var folder = EnsureFolder(path);
            return OperationResult.Ok($"Created folder '{folder.Path}'.", folder.Path);
        }
        catch (Exception ex) { return OperationResult.Fail(ex.Message); }
    }

    /// <summary>Deletes an (empty) folder. Fails if the folder still contains tasks or subfolders.</summary>
    public OperationResult DeleteFolder(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path == "\\")
                return OperationResult.Fail("The root folder cannot be deleted.");

            var folder = _ts.GetFolder(path);
            if (folder.Tasks.Count > 0 || folder.SubFolders.Count > 0)
                return OperationResult.Fail("Folder is not empty. Remove its tasks and subfolders first.");

            var name = folder.Name;
            var parent = _ts.GetFolder(ParentFolder(path));
            parent.DeleteFolder(name, exceptionOnNotExists: false);
            return OperationResult.Ok($"Deleted folder '{path}'.");
        }
        catch (Exception ex) { return OperationResult.Fail(ex.Message); }
    }

    // ---------------------------------------------------------------- Listing

    /// <summary>Lists tasks in a folder, optionally recursing into subfolders.</summary>
    public List<TaskSummaryDto> ListTasks(string folderPath = "\\", bool recursive = false, string? filter = null)
    {
        var result = new List<TaskSummaryDto>();
        var folder = GetFolderOrRoot(folderPath);
        CollectTasks(folder, recursive, result);

        if (!string.IsNullOrWhiteSpace(filter))
        {
            result = result.FindAll(t =>
                t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                t.Path.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                t.ActionsSummary.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private void CollectTasks(TaskFolder folder, bool recursive, List<TaskSummaryDto> sink)
    {
        try
        {
            foreach (var t in folder.GetTasks())
                sink.Add(MapSummary(t));
        }
        catch { /* ignore folders we can't read */ }

        if (!recursive) return;
        try
        {
            foreach (var sub in folder.SubFolders)
                CollectTasks(sub, true, sink);
        }
        catch { /* ignore */ }
    }

    private static TaskSummaryDto MapSummary(Microsoft.Win32.TaskScheduler.Task t)
    {
        var dto = new TaskSummaryDto
        {
            Name = t.Name,
            Path = t.Path,
            Folder = ParentFolder(t.Path),
            Enabled = SafeGet(() => t.Enabled, false),
            State = (TaskRunState)(int)SafeGet(() => t.State, TaskState.Unknown),
            LastRunTime = SafeDate(() => t.LastRunTime),
            NextRunTime = SafeDate(() => t.NextRunTime),
            LastTaskResult = SafeGet(() => (long)t.LastTaskResult, 0L),
        };
        dto.LastResultText = DescribeResult(dto.LastTaskResult);
        try
        {
            var def = t.Definition;
            dto.Author = def.RegistrationInfo.Author ?? string.Empty;
            dto.Description = def.RegistrationInfo.Description ?? string.Empty;
            dto.Source = def.RegistrationInfo.Source ?? string.Empty;
            dto.TriggersSummary = string.Join("; ", SummarizeTriggers(def));
            dto.ActionsSummary = string.Join("; ", SummarizeActions(def));
        }
        catch { /* some protected tasks throw on Definition */ }
        return dto;
    }

    // ---------------------------------------------------------------- Detail

    /// <summary>Returns the full editable detail for a task, or null if not found.</summary>
    public TaskDetailDto? GetTask(string taskPath)
    {
        var t = _ts.GetTask(taskPath);
        if (t == null) return null;

        var def = t.Definition;
        var dto = new TaskDetailDto
        {
            Name = t.Name,
            Path = t.Path,
            Folder = ParentFolder(t.Path),
            Author = def.RegistrationInfo.Author ?? string.Empty,
            Description = def.RegistrationInfo.Description ?? string.Empty,
            Enabled = SafeGet(() => t.Enabled, true),
            State = (TaskRunState)(int)SafeGet(() => t.State, TaskState.Unknown),
            LastRunTime = SafeDate(() => t.LastRunTime),
            NextRunTime = SafeDate(() => t.NextRunTime),
            LastTaskResult = SafeGet(() => (long)t.LastTaskResult, 0L),
            MissedRuns = SafeGet(() => t.NumberOfMissedRuns, 0),
            Xml = SafeGet(() => t.Xml, string.Empty),
        };
        dto.LastResultText = DescribeResult(dto.LastTaskResult);
        dto.Principal = MapPrincipal(def.Principal);
        dto.Settings = MapSettings(def.Settings);
        foreach (var tr in def.Triggers) dto.Triggers.Add(MapTrigger(tr));
        foreach (var ac in def.Actions) dto.Actions.Add(MapAction(ac));
        return dto;
    }

    /// <summary>Returns the raw Task Scheduler XML for a task (the cross-app interchange format).</summary>
    public string ExportXml(string taskPath)
    {
        var t = _ts.GetTask(taskPath) ?? throw new InvalidOperationException($"Task not found: {taskPath}");
        return t.Xml;
    }

    // ---------------------------------------------------------------- Lifecycle ops

    public OperationResult RunTask(string taskPath)
    {
        try
        {
            var t = _ts.GetTask(taskPath);
            if (t == null) return OperationResult.Fail($"Task not found: {taskPath}");
            t.Run();
            return OperationResult.Ok($"Started '{t.Name}'.", t.Path);
        }
        catch (Exception ex) { return OperationResult.Fail(ex.Message); }
    }

    public OperationResult StopTask(string taskPath)
    {
        try
        {
            var t = _ts.GetTask(taskPath);
            if (t == null) return OperationResult.Fail($"Task not found: {taskPath}");
            t.Stop();
            return OperationResult.Ok($"Stopped '{t.Name}'.", t.Path);
        }
        catch (Exception ex) { return OperationResult.Fail(ex.Message); }
    }

    public OperationResult SetEnabled(string taskPath, bool enabled)
    {
        try
        {
            var t = _ts.GetTask(taskPath);
            if (t == null) return OperationResult.Fail($"Task not found: {taskPath}");
            t.Enabled = enabled;
            return OperationResult.Ok($"{(enabled ? "Enabled" : "Disabled")} '{t.Name}'.", t.Path);
        }
        catch (Exception ex) { return OperationResult.Fail(ex.Message); }
    }

    public OperationResult DeleteTask(string taskPath)
    {
        try
        {
            var t = _ts.GetTask(taskPath);
            if (t == null) return OperationResult.Fail($"Task not found: {taskPath}");
            var folder = _ts.GetFolder(ParentFolder(taskPath));
            folder.DeleteTask(t.Name, exceptionOnNotExists: false);
            return OperationResult.Ok($"Deleted '{t.Name}'.");
        }
        catch (Exception ex) { return OperationResult.Fail(ex.Message); }
    }

    // ---------------------------------------------------------------- Create / import

    /// <summary>Registers a task from raw Task Scheduler XML (full fidelity, cross-app safe).</summary>
    public OperationResult ImportXml(string folderPath, string name, string xml, string? userId = null, string? password = null)
    {
        try
        {
            var folder = EnsureFolder(folderPath);
            var logon = string.IsNullOrEmpty(userId) ? TaskLogonType.InteractiveToken : TaskLogonType.Password;
            folder.RegisterTask(name, xml, TaskCreation.CreateOrUpdate, userId, password, logon);
            return OperationResult.Ok($"Imported '{name}'.", CombinePath(folderPath, name));
        }
        catch (Exception ex) { return OperationResult.Fail(ex.Message); }
    }

    /// <summary>Creates or updates a task from structured data, or from raw XML when provided.</summary>
    public OperationResult CreateOrUpdate(TaskCreateRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return OperationResult.Fail("Task name is required.");

            if (!string.IsNullOrWhiteSpace(req.Xml))
                return ImportXml(req.Folder, req.Name, req.Xml!, req.UserId, req.Password);

            if (req.Actions.Count == 0)
                return OperationResult.Fail("At least one action is required.");

            var td = _ts.NewTask();
            td.RegistrationInfo.Author = string.IsNullOrEmpty(req.Author) ? Environment.UserName : req.Author;
            td.RegistrationInfo.Description = req.Description;
            td.RegistrationInfo.Source = AppSource;

            ApplyPrincipal(td.Principal, req.Principal);
            ApplySettings(td.Settings, req.Settings);
            foreach (var tr in req.Triggers) td.Triggers.Add(BuildTrigger(tr));
            foreach (var ac in req.Actions) td.Actions.Add(BuildAction(ac));

            var folder = EnsureFolder(req.Folder);
            if (!string.IsNullOrEmpty(req.UserId))
            {
                folder.RegisterTaskDefinition(req.Name, td, TaskCreation.CreateOrUpdate,
                    req.UserId, req.Password, td.Principal.LogonType);
            }
            else
            {
                folder.RegisterTaskDefinition(req.Name, td);
            }

            return OperationResult.Ok($"Saved '{req.Name}'.", CombinePath(req.Folder, req.Name));
        }
        catch (Exception ex) { return OperationResult.Fail(ex.Message); }
    }

    // ---------------------------------------------------------------- Running

    public List<RunningTaskDto> GetRunningTasks()
    {
        var list = new List<RunningTaskDto>();
        try
        {
            foreach (var rt in _ts.GetRunningTasks(true))
            {
                list.Add(new RunningTaskDto
                {
                    Name = rt.Name,
                    Path = rt.Path,
                    CurrentAction = SafeGet(() => rt.CurrentAction ?? string.Empty, string.Empty),
                    EnginePid = SafeGet(() => rt.EnginePID, 0u),
                    State = (TaskRunState)(int)SafeGet(() => rt.State, TaskState.Unknown),
                    Source = SafeGet(() => rt.Definition.RegistrationInfo.Source ?? string.Empty, string.Empty),
                });
            }
        }
        catch { /* ignore */ }
        return list;
    }

    /// <summary>Reads recent entries from the Task Scheduler operational event log for a task.</summary>
    public List<TaskHistoryEntryDto> GetTaskHistory(string taskPath, int max = 60)
    {
        var list = new List<TaskHistoryEntryDto>();
        try
        {
            var log = new TaskEventLog(taskPath);
            foreach (var e in log)
            {
                list.Add(new TaskHistoryEntryDto
                {
                    TimeCreated = e.TimeCreated,
                    EventId = e.EventId,
                    Level = e.Level ?? string.Empty,
                    Category = e.TaskCategory ?? string.Empty,
                    OpCode = e.OpCode ?? string.Empty,
                });
                if (list.Count >= max) break;
            }
        }
        catch { /* history channel may be disabled or inaccessible */ }
        return list;
    }

    /// <summary>
    /// Produces a grounded, evidence-based assessment of a task derived solely from its definition
    /// (actions, triggers, principal, settings, last result). Every finding cites the concrete value
    /// it is based on; the overall risk is computed from those findings, not speculation.
    /// </summary>
    public TaskAnalysisDto AnalyzeTask(string taskPath)
    {
        var detail = GetTask(taskPath);
        if (detail is null)
            return new TaskAnalysisDto { Path = taskPath, Found = false, Summary = $"Task not found: {taskPath}" };

        var result = new TaskAnalysisDto { Path = detail.Path, Name = detail.Name };
        var findings = result.Findings;

        // ---- Observed facts (the grounding) ----
        result.ObservedFacts.Add($"Runs as: {(string.IsNullOrEmpty(detail.Principal.UserId) ? "(current user)" : detail.Principal.UserId)} ({detail.Principal.LogonType})");
        result.ObservedFacts.Add($"Highest privileges: {detail.Principal.RunWithHighestPrivileges}");
        result.ObservedFacts.Add($"Hidden: {detail.Settings.Hidden}, Enabled: {detail.Enabled}");
        result.ObservedFacts.Add($"Last result: {detail.LastResultText}");
        foreach (var t in detail.Triggers) result.ObservedFacts.Add($"Trigger: {t.Summary}");
        foreach (var a in detail.Actions) result.ObservedFacts.Add($"Action: {a.Summary}");

        // ---- Principal / settings findings ----
        var runsElevated = detail.Principal.RunWithHighestPrivileges;
        var runsAsSystem = detail.Principal.UserId.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase)
                           || detail.Principal.LogonType.Equals("ServiceAccount", StringComparison.OrdinalIgnoreCase);
        if (runsElevated)
            findings.Add(new AnalysisFinding { Severity = AnalysisSeverity.Notice, Title = "Runs with highest privileges", Evidence = "Principal RunLevel = HighestAvailable" });
        if (runsAsSystem)
            findings.Add(new AnalysisFinding { Severity = AnalysisSeverity.Notice, Title = "Runs as a system/service account", Evidence = $"UserId '{detail.Principal.UserId}', LogonType '{detail.Principal.LogonType}'" });
        if (detail.Settings.Hidden)
            findings.Add(new AnalysisFinding { Severity = AnalysisSeverity.Notice, Title = "Task is hidden", Evidence = "Settings Hidden = true (not shown by default in Task Scheduler)" });

        var autoStarts = detail.Triggers.Any(t => t.Kind is TriggerKind.AtStartup or TriggerKind.AtLogOn);
        var hasWarning = false;

        // ---- Action (command line) findings ----
        foreach (var action in detail.Actions.Where(a => a.Kind == ActionKind.Exec))
        {
            var cmd = action.Command ?? string.Empty;
            var args = action.Arguments ?? string.Empty;
            var line = ($"{cmd} {args}").ToLowerInvariant();

            foreach (var (needle, title) in SuspiciousCommandPatterns)
            {
                if (line.Contains(needle))
                {
                    hasWarning = true;
                    findings.Add(new AnalysisFinding
                    {
                        Severity = AnalysisSeverity.Warning,
                        Title = title,
                        Evidence = Snippet(action.Summary, needle),
                    });
                }
            }

            foreach (var loc in UserWritableLocations)
            {
                if (cmd.Contains(loc, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new AnalysisFinding
                    {
                        Severity = AnalysisSeverity.Warning,
                        Title = "Runs a program from a user-writable location",
                        Evidence = $"Command path '{cmd}' is under '{loc}', a location commonly abused for persistence",
                    });
                    hasWarning = true;
                }
            }
        }

        // ---- Combination / persistence pattern ----
        if (autoStarts && detail.Settings.Hidden && runsElevated)
            findings.Add(new AnalysisFinding
            {
                Severity = AnalysisSeverity.Warning,
                Title = "Hidden, elevated, auto-start combination",
                Evidence = "Task is hidden, runs with highest privileges, and starts at boot/logon \u2014 a common persistence pattern",
            });

        // ---- Last result ----
        if (detail.LastTaskResult != 0 && detail.LastTaskResult != 0x00041303 /* not yet run */)
            findings.Add(new AnalysisFinding { Severity = AnalysisSeverity.Info, Title = "Last run did not report success", Evidence = detail.LastResultText });

        // ---- Risk + grounded summary ----
        // De-duplicate findings that share a title (e.g. -enc and -encodedcommand aliases).
        var deduped = findings
            .GroupBy(f => f.Title, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        result.Findings = deduped;

        var strongPattern = deduped.Any(f => f.Severity == AnalysisSeverity.Warning
            && (f.Title.Contains("user-writable") || f.Title.Contains("persistence") || f.Title.StartsWith("Obfuscated") || f.Title.Contains("Downloads")));
        result.Risk = strongPattern && (detail.Settings.Hidden || runsElevated) ? RiskLevel.High
            : hasWarning ? RiskLevel.Medium
            : RiskLevel.Low;

        var warnings = deduped.Count(f => f.Severity == AnalysisSeverity.Warning);
        var notices = deduped.Count(f => f.Severity == AnalysisSeverity.Notice);
        result.Summary = result.Risk switch
        {
            RiskLevel.High => $"High concern: {warnings} warning(s) grounded in the task's command line/configuration (see evidence). Review before trusting this task.",
            RiskLevel.Medium => $"Some concern: {warnings} warning(s) and {notices} notice(s) based on the task's configuration. Worth a closer look.",
            _ => notices > 0
                ? $"Low concern: no suspicious command patterns found; {notices} notice(s) about privileges/visibility only."
                : "Low concern: the task's command, triggers and settings show no known suspicious patterns.",
        };
        result.Summary += " This assessment is based only on the task's stored definition, not on file signatures or runtime behavior.";
        return result;
    }

    private static readonly (string needle, string title)[] SuspiciousCommandPatterns =
    {
        ("-enc", "Obfuscated PowerShell (encoded command)"),
        ("-encodedcommand", "Obfuscated PowerShell (encoded command)"),
        ("-ep bypass", "PowerShell execution-policy bypass"),
        ("-executionpolicy bypass", "PowerShell execution-policy bypass"),
        ("-windowstyle hidden", "Runs with a hidden window"),
        ("-w hidden", "Runs with a hidden window"),
        ("invoke-webrequest", "Downloads content from the web"),
        ("downloadstring", "Downloads and runs remote code"),
        ("downloadfile", "Downloads a file from the web"),
        ("iwr ", "Downloads content from the web"),
        ("certutil", "Uses certutil (often abused to download/decode payloads)"),
        ("bitsadmin", "Uses bitsadmin (often abused to download payloads)"),
        ("mshta", "Uses mshta (often abused to run remote scripts)"),
        ("regsvr32 /i:http", "Uses regsvr32 to run a remote script"),
        ("frombase64string", "Decodes a Base64 payload at runtime"),
    };

    private static readonly string[] UserWritableLocations =
    {
        "\\appdata\\", "\\temp\\", "\\downloads\\", "\\users\\public\\", "%temp%", "%appdata%", "\\windows\\temp\\",
    };

    private static string Snippet(string text, string needle)
    {
        if (string.IsNullOrEmpty(text)) return needle;
        var idx = text.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal);
        if (idx < 0) return text.Length > 160 ? text[..160] + "\u2026" : text;
        var start = Math.Max(0, idx - 30);
        var len = Math.Min(text.Length - start, needle.Length + 60);
        return "\u2026" + text.Substring(start, len) + "\u2026";
    }

    private static PrincipalDto MapPrincipal(TaskPrincipal p) => new()
    {
        UserId = p.Account ?? p.UserId ?? string.Empty,
        LogonType = p.LogonType.ToString(),
        RunWithHighestPrivileges = p.RunLevel == TaskRunLevel.Highest,
    };

    private static SettingsDto MapSettings(TaskSettings s) => new()
    {
        Enabled = s.Enabled,
        Hidden = s.Hidden,
        AllowDemandStart = s.AllowDemandStart,
        StartWhenAvailable = s.StartWhenAvailable,
        RunOnlyIfNetworkAvailable = s.RunOnlyIfNetworkAvailable,
        DisallowStartIfOnBatteries = s.DisallowStartIfOnBatteries,
        StopIfGoingOnBatteries = s.StopIfGoingOnBatteries,
        WakeToRun = s.WakeToRun,
        RunOnlyIfIdle = s.RunOnlyIfIdle,
        RestartOnFailure = s.RestartCount > 0,
        RestartCount = s.RestartCount,
        RestartInterval = ToIso(s.RestartInterval),
        ExecutionTimeLimit = ToIso(s.ExecutionTimeLimit),
        MultipleInstances = s.MultipleInstances.ToString(),
    };

    private static TriggerDto MapTrigger(Trigger tr)
    {
        var dto = new TriggerDto
        {
            Enabled = tr.Enabled,
            RawType = tr.TriggerType.ToString(),
            StartBoundary = NormalizeDate(tr.StartBoundary),
            EndBoundary = NormalizeDate(tr.EndBoundary),
        };
        if (tr.Repetition.Interval != TimeSpan.Zero)
        {
            dto.RepetitionInterval = ToIso(tr.Repetition.Interval);
            dto.RepetitionDuration = ToIso(tr.Repetition.Duration);
        }

        switch (tr)
        {
            case TimeTrigger:
                dto.Kind = TriggerKind.OneTime;
                break;
            case DailyTrigger dt:
                dto.Kind = TriggerKind.Daily;
                dto.DaysInterval = dt.DaysInterval;
                break;
            case WeeklyTrigger wt:
                dto.Kind = TriggerKind.Weekly;
                dto.WeeksInterval = wt.WeeksInterval;
                dto.DaysOfWeek = SplitFlags(wt.DaysOfWeek);
                break;
            case MonthlyTrigger mt:
                dto.Kind = TriggerKind.Monthly;
                dto.DaysOfMonth = new List<int>(mt.DaysOfMonth);
                dto.MonthsOfYear = SplitFlags(mt.MonthsOfYear);
                dto.RunOnLastDayOfMonth = mt.RunOnLastDayOfMonth;
                break;
            case MonthlyDOWTrigger mdt:
                dto.Kind = TriggerKind.MonthlyDOW;
                dto.DaysOfWeek = SplitFlags(mdt.DaysOfWeek);
                dto.MonthsOfYear = SplitFlags(mdt.MonthsOfYear);
                dto.WeeksOfMonth = SplitFlags(mdt.WeeksOfMonth);
                dto.RunOnLastWeek = mdt.RunOnLastWeekOfMonth;
                break;
            case BootTrigger bt:
                dto.Kind = TriggerKind.AtStartup;
                dto.Delay = ToIso(bt.Delay);
                break;
            case LogonTrigger lt:
                dto.Kind = TriggerKind.AtLogOn;
                dto.UserId = lt.UserId;
                dto.Delay = ToIso(lt.Delay);
                break;
            case IdleTrigger:
                dto.Kind = TriggerKind.OnIdle;
                break;
            case EventTrigger et:
                dto.Kind = TriggerKind.OnEvent;
                dto.Subscription = et.Subscription;
                break;
            case SessionStateChangeTrigger sst:
                dto.Kind = TriggerKind.OnSessionStateChange;
                dto.StateChange = sst.StateChange.ToString();
                dto.UserId = sst.UserId;
                dto.Delay = ToIso(sst.Delay);
                break;
            default:
                dto.Kind = TriggerKind.Other;
                break;
        }
        dto.Summary = DescribeTrigger(tr);
        return dto;
    }

    private static ActionDto MapAction(Microsoft.Win32.TaskScheduler.Action ac)
    {
        var dto = new ActionDto();
        switch (ac)
        {
            case ExecAction ea:
                dto.Kind = ActionKind.Exec;
                dto.Command = ea.Path ?? string.Empty;
                dto.Arguments = ea.Arguments ?? string.Empty;
                dto.WorkingDirectory = ea.WorkingDirectory ?? string.Empty;
                break;
            case ComHandlerAction:
                dto.Kind = ActionKind.ComHandler;
                break;
            case EmailAction:
                dto.Kind = ActionKind.Email;
                break;
            case ShowMessageAction:
                dto.Kind = ActionKind.ShowMessage;
                break;
            default:
                dto.Kind = ActionKind.Other;
                break;
        }
        dto.Summary = DescribeAction(ac);
        return dto;
    }

    // ---------------------------------------------------------------- Mapping: write

    private static void ApplyPrincipal(TaskPrincipal p, PrincipalDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.UserId))
            p.UserId = dto.UserId;
        if (Enum.TryParse<TaskLogonType>(dto.LogonType, true, out var lt))
            p.LogonType = lt;
        p.RunLevel = dto.RunWithHighestPrivileges ? TaskRunLevel.Highest : TaskRunLevel.LUA;
    }

    private static void ApplySettings(TaskSettings s, SettingsDto dto)
    {
        s.Enabled = dto.Enabled;
        s.Hidden = dto.Hidden;
        s.AllowDemandStart = dto.AllowDemandStart;
        s.StartWhenAvailable = dto.StartWhenAvailable;
        s.RunOnlyIfNetworkAvailable = dto.RunOnlyIfNetworkAvailable;
        s.DisallowStartIfOnBatteries = dto.DisallowStartIfOnBatteries;
        s.StopIfGoingOnBatteries = dto.StopIfGoingOnBatteries;
        s.WakeToRun = dto.WakeToRun;
        s.RunOnlyIfIdle = dto.RunOnlyIfIdle;
        s.ExecutionTimeLimit = FromIso(dto.ExecutionTimeLimit);
        if (Enum.TryParse<TaskInstancesPolicy>(dto.MultipleInstances, true, out var mip))
            s.MultipleInstances = mip;
        if (dto.RestartOnFailure && dto.RestartCount > 0)
        {
            s.RestartCount = dto.RestartCount;
            s.RestartInterval = FromIso(dto.RestartInterval, TimeSpan.FromMinutes(1));
        }
    }

    private static Trigger BuildTrigger(TriggerDto dto)
    {
        Trigger tr = dto.Kind switch
        {
            TriggerKind.OneTime => new TimeTrigger { StartBoundary = dto.StartBoundary ?? DateTime.Now },
            TriggerKind.Daily => new DailyTrigger((short)Math.Max(1, dto.DaysInterval))
            { StartBoundary = dto.StartBoundary ?? DateTime.Now },
            TriggerKind.Weekly => new WeeklyTrigger(JoinDays(dto.DaysOfWeek), (short)Math.Max(1, dto.WeeksInterval))
            { StartBoundary = dto.StartBoundary ?? DateTime.Now },
            TriggerKind.Monthly => new MonthlyTrigger
            {
                StartBoundary = dto.StartBoundary ?? DateTime.Now,
                DaysOfMonth = dto.DaysOfMonth.Count > 0 ? dto.DaysOfMonth.ToArray() : new[] { 1 },
                MonthsOfYear = JoinMonths(dto.MonthsOfYear),
                RunOnLastDayOfMonth = dto.RunOnLastDayOfMonth,
            },
            TriggerKind.MonthlyDOW => new MonthlyDOWTrigger
            {
                StartBoundary = dto.StartBoundary ?? DateTime.Now,
                DaysOfWeek = JoinDays(dto.DaysOfWeek),
                MonthsOfYear = JoinMonths(dto.MonthsOfYear),
                WeeksOfMonth = JoinWeeks(dto.WeeksOfMonth),
                RunOnLastWeekOfMonth = dto.RunOnLastWeek,
            },
            TriggerKind.AtStartup => new BootTrigger(),
            TriggerKind.AtLogOn => new LogonTrigger(),
            TriggerKind.OnIdle => new IdleTrigger(),
            TriggerKind.OnEvent => new EventTrigger { Subscription = dto.Subscription ?? string.Empty },
            TriggerKind.OnSessionStateChange => new SessionStateChangeTrigger
            {
                StartBoundary = dto.StartBoundary ?? DateTime.Now,
                StateChange = ParseStateChange(dto.StateChange),
                UserId = string.IsNullOrWhiteSpace(dto.UserId) ? null : dto.UserId,
            },
            _ => new TimeTrigger { StartBoundary = dto.StartBoundary ?? DateTime.Now },
        };

        tr.Enabled = dto.Enabled;
        if (dto.EndBoundary.HasValue) tr.EndBoundary = dto.EndBoundary.Value;
        if (tr is LogonTrigger lt && !string.IsNullOrWhiteSpace(dto.UserId)) lt.UserId = dto.UserId;
        if (!string.IsNullOrWhiteSpace(dto.Delay))
        {
            var delay = FromIso(dto.Delay);
            if (tr is BootTrigger bt) bt.Delay = delay;
            else if (tr is LogonTrigger lt2) lt2.Delay = delay;
            else if (tr is SessionStateChangeTrigger sst2) sst2.Delay = delay;
        }
        if (!string.IsNullOrWhiteSpace(dto.RepetitionInterval))
        {
            tr.Repetition.Interval = FromIso(dto.RepetitionInterval);
            if (!string.IsNullOrWhiteSpace(dto.RepetitionDuration))
                tr.Repetition.Duration = FromIso(dto.RepetitionDuration);
        }
        return tr;
    }

    private static Microsoft.Win32.TaskScheduler.Action BuildAction(ActionDto dto)
    {
        return new ExecAction(
            dto.Command,
            string.IsNullOrWhiteSpace(dto.Arguments) ? null : dto.Arguments,
            string.IsNullOrWhiteSpace(dto.WorkingDirectory) ? null : dto.WorkingDirectory);
    }

    // ---------------------------------------------------------------- Summaries

    private static IEnumerable<string> SummarizeTriggers(TaskDefinition def)
    {
        foreach (var tr in def.Triggers) yield return DescribeTrigger(tr);
    }

    private static IEnumerable<string> SummarizeActions(TaskDefinition def)
    {
        foreach (var ac in def.Actions) yield return DescribeAction(ac);
    }

    private static string DescribeTrigger(Trigger tr) => tr switch
    {
        TimeTrigger t => $"Once at {t.StartBoundary:g}",
        DailyTrigger d => $"Daily every {d.DaysInterval} day(s) at {d.StartBoundary:t}",
        WeeklyTrigger w => $"Weekly ({w.DaysOfWeek}) at {w.StartBoundary:t}",
        MonthlyTrigger m => $"Monthly on day {string.Join(",", m.DaysOfMonth)}",
        MonthlyDOWTrigger md => $"Monthly ({md.DaysOfWeek})",
        BootTrigger => "At system startup",
        LogonTrigger l => string.IsNullOrEmpty(l.UserId) ? "At log on (any user)" : $"At log on ({l.UserId})",
        IdleTrigger => "On idle",
        EventTrigger => "On event",
        RegistrationTrigger => "When task is created",
        SessionStateChangeTrigger => "On session state change",
        _ => tr.TriggerType.ToString(),
    };

    private static string DescribeAction(Microsoft.Win32.TaskScheduler.Action ac) => ac switch
    {
        ExecAction e => string.IsNullOrWhiteSpace(e.Arguments) ? e.Path : $"{e.Path} {e.Arguments}",
        ComHandlerAction c => $"COM handler {c.ClassId}",
        EmailAction => "Send e-mail (deprecated)",
        ShowMessageAction => "Show message (deprecated)",
        _ => ac.ActionType.ToString(),
    };

    // ---------------------------------------------------------------- Helpers

    private TaskFolder GetFolderOrRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "\\") return _ts.RootFolder;
        try { return _ts.GetFolder(path); }
        catch { return _ts.RootFolder; }
    }

    private TaskFolder EnsureFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "\\") return _ts.RootFolder;

        try
        {
            var existing = _ts.GetFolder(path);
            if (existing != null) return existing;
        }
        catch { /* needs creating, walk down */ }

        var current = _ts.RootFolder;
        var accum = string.Empty;
        foreach (var part in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            accum += "\\" + part;
            TaskFolder? next = null;
            try { next = _ts.GetFolder(accum); } catch { next = null; }
            if (next == null)
            {
                try { next = current.CreateFolder(part); }
                catch { next = _ts.GetFolder(accum); }
            }
            current = next ?? throw new InvalidOperationException($"Could not create folder '{accum}'.");
        }
        return current;
    }

    private static string ParentFolder(string taskPath)
    {
        var idx = taskPath.LastIndexOf('\\');
        if (idx <= 0) return "\\";
        return taskPath.Substring(0, idx);
    }

    private static string CombinePath(string folder, string name)
    {
        folder = string.IsNullOrEmpty(folder) ? "\\" : folder;
        return folder.EndsWith('\\') ? folder + name : folder + "\\" + name;
    }

    private static List<string> SplitFlags<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var list = new List<string>();
        foreach (TEnum v in Enum.GetValues<TEnum>())
        {
            var l = Convert.ToInt64(v);
            if (l == 0) continue;
            // skip composite "all" members (more than one bit set)
            if ((l & (l - 1)) != 0) continue;
            if (value.HasFlag(v)) list.Add(v.ToString());
        }
        return list;
    }

    private static DaysOfTheWeek JoinDays(IEnumerable<string> names)
    {
        DaysOfTheWeek result = 0;
        foreach (var n in names)
            if (Enum.TryParse<DaysOfTheWeek>(n, true, out var d)) result |= d;
        return result == 0 ? DaysOfTheWeek.AllDays : result;
    }

    private static MonthsOfTheYear JoinMonths(IEnumerable<string> names)
    {
        MonthsOfTheYear result = 0;
        foreach (var n in names)
            if (Enum.TryParse<MonthsOfTheYear>(n, true, out var m)) result |= m;
        return result == 0 ? MonthsOfTheYear.AllMonths : result;
    }

    private static WhichWeek JoinWeeks(IEnumerable<string> names)
    {
        WhichWeek result = 0;
        foreach (var n in names)
            if (Enum.TryParse<WhichWeek>(n, true, out var w)) result |= w;
        return result == 0 ? WhichWeek.FirstWeek : result;
    }

    private static TaskSessionStateChangeType ParseStateChange(string? s) =>
        Enum.TryParse<TaskSessionStateChangeType>(s, true, out var v) ? v : TaskSessionStateChangeType.ConsoleConnect;

    private static string ToIso(TimeSpan ts) => ts == TimeSpan.Zero ? string.Empty : XmlConvert.ToString(ts);

    private static TimeSpan FromIso(string iso, TimeSpan fallback = default)
    {
        if (string.IsNullOrWhiteSpace(iso)) return fallback;
        try { return XmlConvert.ToTimeSpan(iso); }
        catch { return TimeSpan.TryParse(iso, out var ts) ? ts : fallback; }
    }

    private static DateTime? NormalizeDate(DateTime dt)
    {
        if (dt == DateTime.MinValue || dt == DateTime.MaxValue) return null;
        if (dt.Year <= 1900 || dt.Year >= 9999) return null;
        return dt;
    }

    private static DateTime? SafeDate(Func<DateTime> getter)
    {
        try { return NormalizeDate(getter()); } catch { return null; }
    }

    private static T SafeGet<T>(Func<T> getter, T fallback)
    {
        try { return getter(); } catch { return fallback; }
    }

    private static string DescribeResult(long code) => code switch
    {
        0 => "Success (0x0)",
        0x00041300 => "Ready (0x41300)",
        0x00041301 => "Running (0x41301)",
        0x00041302 => "Disabled (0x41302)",
        0x00041303 => "Has not run yet (0x41303)",
        0x00041304 => "No more runs (0x41304)",
        0x00041306 => "Terminated by user (0x41306)",
        0x80070002 => "File not found (0x80070002)",
        _ => $"0x{code & 0xFFFFFFFF:X8}",
    };

    public void Dispose() => _ts.Dispose();
}
