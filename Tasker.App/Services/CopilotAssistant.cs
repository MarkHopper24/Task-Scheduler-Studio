using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;
using Tasker.Core;
using Windows.Security.Credentials;

// GHCP001: PermissionDecision (custom permission handler) is marked experimental by the Copilot SDK.
// We rely on it deliberately to enforce the safety boundary; opt in here.
#pragma warning disable GHCP001

namespace Tasker_App.Services;

/// <summary>
/// Embeds the GitHub Copilot SDK to drive a guided, natural-language task-creation flow. The
/// agent reasons over the user's prompt, asks clarifying questions when details are missing, and
/// calls the same tools the Tasker MCP server exposes (create_task, list_folders, list_tasks) —
/// all backed by the shared <see cref="TaskerService"/>, so created tasks land in the live Windows
/// store. Authentication uses a stored GitHub token when present, otherwise the signed-in Copilot
/// user.
/// </summary>
public sealed class CopilotAssistant : IAsyncDisposable
{
    private const string VaultResource = "WindowsTasker.Copilot";
    private const string VaultUser = "github-token";
    private const string DefaultModelId = "auto";

    private string _model = DefaultModelId;
    private string? _reasoningEffort;

    /// <summary>Model id used for new sessions. "auto" lets Copilot pick. Changing it takes effect
    /// on the next session (callers should <see cref="ResetAsync"/> after changing).</summary>
    public string SelectedModel
    {
        get => _model;
        set => _model = string.IsNullOrWhiteSpace(value) ? DefaultModelId : value.Trim();
    }

    /// <summary>Optional model-specific reasoning effort (e.g. "low"/"medium"/"high"); null = model default.</summary>
    public string? SelectedReasoningEffort
    {
        get => _reasoningEffort;
        set => _reasoningEffort = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private CopilotClient? _client;
    private CopilotSession? _session;

    /// <summary>When true, the assistant routes tool calls through the bundled Tasker MCP server
    /// (a separate process) instead of equivalent in-process tools.</summary>
    public bool UseMcpServer { get; set; }

    public static string McpExePath
    {
        get
        {
            // Packaged runtime base dir contains the bundled "mcp" folder; fall back to the
            // package install location and the dev build-output sibling folder.
            foreach (var root in new[] { AppContext.BaseDirectory, TryPackagePath() })
            {
                if (string.IsNullOrEmpty(root)) continue;
                var candidate = System.IO.Path.Combine(root!, "mcp", "Tasker.Mcp.exe");
                if (System.IO.File.Exists(candidate)) return candidate;
            }
            return System.IO.Path.Combine(AppContext.BaseDirectory, "mcp", "Tasker.Mcp.exe");
        }
    }

    /// <summary>Stable app-execution alias declared in the package manifest for the bundled MCP
    /// server. Resolvable on PATH (via %LOCALAPPDATA%\Microsoft\WindowsApps) once the app is
    /// installed; unaffected by version/install-location changes.</summary>
    public const string McpAlias = "WindowsTaskerMcp.exe";

    private static bool IsPackaged => TryPackagePath() is not null;

    /// <summary>The command external MCP clients (and the in-app MCP routing) should launch to start
    /// the bundled server. For an installed/packaged app this is the stable alias <see cref="McpAlias"/>,
    /// which survives updates and works from any install path; for an unpackaged dev run it's the
    /// absolute path to the built server exe.</summary>
    public static string McpServerCommand => IsPackaged ? McpAlias : McpExePath;

    private static string? TryPackagePath()
    {
        try { return Windows.ApplicationModel.Package.Current.InstalledLocation.Path; }
        catch { return null; }
    }

    public static bool McpAvailable => System.IO.File.Exists(McpExePath);

    /// <summary>Path of the most recently created/updated task (set by the create tools).</summary>
    public string? LastCreatedTaskPath { get; private set; }

    public bool IsReady => _session is not null;

    public event Action<string>? AssistantMessage;
    public event Action<string>? ToolActivity;
    public event Action<string>? ErrorMessage;

    // ---------------------------------------------------------------- auth

    public bool HasStoredToken => TryGetToken() is not null;

    public void StoreToken(string token)
    {
        ClearToken();
        var vault = new PasswordVault();
        vault.Add(new PasswordCredential(VaultResource, VaultUser, token.Trim()));
    }

    public void ClearToken()
    {
        try
        {
            var vault = new PasswordVault();
            foreach (var c in vault.FindAllByResource(VaultResource))
                vault.Remove(c);
        }
        catch { /* nothing stored */ }
    }

    private static string? TryGetToken()
    {
        try
        {
            var vault = new PasswordVault();
            var cred = vault.Retrieve(VaultResource, VaultUser);
            cred.RetrievePassword();
            return string.IsNullOrWhiteSpace(cred.Password) ? null : cred.Password;
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------- lifecycle

    public async Task<bool> EnsureStartedAsync()
    {
        if (!AppSettings.AiEnabled)
        {
            ErrorMessage?.Invoke("AI features are turned off in Settings. Enable them to use the assistant.");
            return false;
        }
        if (_session is not null) return true;
        await _startGate.WaitAsync();
        try
        {
            if (_session is not null) return true;

            var options = new CopilotClientOptions();
            var token = TryGetToken();
            if (!string.IsNullOrWhiteSpace(token)) options.GitHubToken = token;

            // Run the agent in a locked-down workspace that carries the hardened AGENTS.md.
            var workspace = PrepareAgentWorkspace();
            options.WorkingDirectory = workspace;

            var useMcp = UseMcpServer && McpAvailable;
            if (useMcp)
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    ErrorMessage?.Invoke("MCP-server mode needs a GitHub token (with Copilot access). Add one below, or turn the toggle off to use the built-in tools.");
                    return false;
                }
                options.BaseDirectory = PrepareMcpHome();
            }

            _client = new CopilotClient(options);
            await _client.StartAsync();

            var config = new SessionConfig
            {
                Model = SelectedModel,
                WorkingDirectory = workspace,
                // Hard safety boundary: only the Windows Tasker task tools may execute. Every other
                // capability the bundled CLI exposes (shell, file read/write, web, memory, hooks,
                // extensions) is rejected here, regardless of what the model attempts.
                OnPermissionRequest = HandlePermissionAsync,
                ExcludedTools = DangerousBuiltInTools,
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Append,
                    Content = BuildSystemPrompt(),
                },
            };

            if (!useMcp)
                config.Tools = BuildInProcessTools();

            if (!string.IsNullOrWhiteSpace(SelectedReasoningEffort))
                config.ReasoningEffort = SelectedReasoningEffort;

            _session = await _client.CreateSessionAsync(config);

            _session.On<SessionEvent>(OnSessionEvent);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage?.Invoke(FriendlyError(ex));
            await DisposeSessionAsync();
            return false;
        }
        finally
        {
            _startGate.Release();
        }
    }

    private List<AIFunctionDeclaration> BuildInProcessTools() =>
    [
        CopilotTool.DefineTool(CreateTaskAsync,
            toolOptions: new CopilotToolOptions { SkipPermission = true },
            factoryOptions: new AIFunctionFactoryOptions
            {
                Name = "create_task",
                Description = "Create or update a Windows scheduled task with one schedule and one program/command to run.",
            }),
        CopilotTool.DefineTool(CreateTaskFromXmlAsync,
            toolOptions: new CopilotToolOptions { SkipPermission = true },
            factoryOptions: new AIFunctionFactoryOptions
            {
                Name = "create_task_from_xml",
                Description = "Create or update a task from raw Task Scheduler XML (full fidelity).",
            }),
        CopilotTool.DefineTool(ListFoldersAsync,
            toolOptions: new CopilotToolOptions { SkipPermission = true },
            factoryOptions: new AIFunctionFactoryOptions { Name = "list_folders", Description = "List existing Task Scheduler folder paths." }),
        CopilotTool.DefineTool(ListTasksAsync,
            toolOptions: new CopilotToolOptions { SkipPermission = true },
            factoryOptions: new AIFunctionFactoryOptions
            {
                Name = "list_tasks",
                Description = "List existing scheduled tasks in a folder (to avoid duplicate names, inspect current tasks, or answer questions like 'what runs at startup?').",
            }),
        CopilotTool.DefineTool(GetTaskAsync,
            toolOptions: new CopilotToolOptions { SkipPermission = true },
            factoryOptions: new AIFunctionFactoryOptions
            {
                Name = "get_task",
                Description = "Get the full definition (triggers, actions, settings, state) of one task by its path.",
            }),
        CopilotTool.DefineTool(AnalyzeTaskAsync,
            toolOptions: new CopilotToolOptions { SkipPermission = true },
            factoryOptions: new AIFunctionFactoryOptions
            {
                Name = "analyze_task",
                Description = "Parse a task and return a grounded, evidence-based risk assessment (risk level + findings that each cite the exact command/trigger/setting). Use this to judge whether a task looks legitimate or suspicious.",
            }),
        CopilotTool.DefineTool(RunTaskAsync,
            toolOptions: new CopilotToolOptions { SkipPermission = true },
            factoryOptions: new AIFunctionFactoryOptions { Name = "run_task", Description = "Start a task immediately by its path." }),
        CopilotTool.DefineTool(StopTaskAsync,
            toolOptions: new CopilotToolOptions { SkipPermission = true },
            factoryOptions: new AIFunctionFactoryOptions { Name = "stop_task", Description = "Stop a running task by its path." }),
        CopilotTool.DefineTool(EnableTaskAsync,
            toolOptions: new CopilotToolOptions { SkipPermission = true },
            factoryOptions: new AIFunctionFactoryOptions { Name = "enable_task", Description = "Enable a task by its path." }),
        CopilotTool.DefineTool(DisableTaskAsync,
            toolOptions: new CopilotToolOptions { SkipPermission = true },
            factoryOptions: new AIFunctionFactoryOptions { Name = "disable_task", Description = "Disable a task by its path." }),
        CopilotTool.DefineTool(DeleteTaskAsync,
            toolOptions: new CopilotToolOptions { SkipPermission = true },
            factoryOptions: new AIFunctionFactoryOptions { Name = "delete_task", Description = "Permanently delete a task by its path. Confirm with the user first." }),
    ];

    /// <summary>Creates an isolated COPILOT_HOME containing an mcp-config.json that registers the
    /// bundled Tasker MCP server, and returns its path for <c>CopilotClientOptions.BaseDirectory</c>.</summary>
    private static string PrepareMcpHome()
    {
        var home = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsTasker", "copilot-home");
        System.IO.Directory.CreateDirectory(home);

        var config = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["windows-tasker"] = new JsonObject
                {
                    ["name"] = "windows-tasker",
                    ["type"] = "stdio",
                    // Launch the bundled server by absolute path, not the WindowsTaskerMcp.exe alias:
                    // a packaged app can't reliably launch its OWN app-execution alias. The path is
                    // resolved fresh at runtime (AppContext.BaseDirectory), so it's never stale. The
                    // alias is only for EXTERNAL clients (see McpInstaller), where update-stability matters.
                    ["command"] = McpExePath,
                    ["args"] = new JsonArray(),
                    ["tools"] = new JsonArray(),
                    ["enabled"] = true,
                },
            },
        };
        System.IO.File.WriteAllText(System.IO.Path.Combine(home, "mcp-config.json"), config.ToJsonString());
        return home;
    }

    // ---------------------------------------------------------------- safety / hardening

    /// <summary>The only tools the assistant is ever allowed to execute.</summary>
    private static readonly HashSet<string> AllowedToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "create_task", "create_task_from_xml", "list_folders", "list_tasks", "get_task",
        "analyze_task", "run_task", "stop_task", "enable_task", "disable_task", "delete_task",
    };

    /// <summary>Built-in CLI capabilities explicitly disabled so they are never offered to the model.</summary>
    private static readonly List<string> DangerousBuiltInTools = new()
    {
        "shell", "bash", "run_in_terminal", "execute", "powershell",
        "edit_file", "create_file", "write_file", "str_replace_editor", "apply_patch",
        "read_file", "view", "fetch", "web_fetch", "web_search", "browser",
        "memory", "store_memory", "manage_schedule",
    };

    /// <summary>Approves only the Windows Tasker task tools; rejects shell, file, web, memory, and
    /// every other capability — the hard safety boundary for the embedded agent.</summary>
    private static Task<PermissionDecision> HandlePermissionAsync(PermissionRequest request, PermissionInvocation invocation)
    {
        PermissionDecision decision = request switch
        {
            PermissionRequestMcp mcp when string.Equals(mcp.ServerName, "windows-tasker", StringComparison.OrdinalIgnoreCase)
                                          && AllowedToolNames.Contains(mcp.ToolName ?? string.Empty)
                => PermissionDecision.ApproveOnce(),
            PermissionRequestCustomTool tool when AllowedToolNames.Contains(tool.ToolName ?? string.Empty)
                => PermissionDecision.ApproveOnce(),
            _ => PermissionDecision.Reject(
                "Blocked: this assistant may only create and manage Windows scheduled tasks. It cannot run shell commands, read or write files, access the web, or use any other tool."),
        };
        return Task.FromResult(decision);
    }

    /// <summary>Writes the hardened AGENTS.md into an isolated working directory and returns it.</summary>
    private static string PrepareAgentWorkspace()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsTasker", "agent");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "AGENTS.md"), AgentInstructions);
        return dir;
    }

    private const string AgentInstructions = """
        # Windows Tasker Agent

        You are an assistant embedded in the **Windows Tasker** desktop app. Your sole purpose is to
        help the user **create, inspect, and manage Windows Scheduled Tasks** through the provided
        Windows Tasker tools.

        ## Allowed tools (the ONLY tools you may use)
        - create_task, create_task_from_xml
        - list_folders, list_tasks, get_task, analyze_task
        - run_task, stop_task, enable_task, disable_task, delete_task

        ## Analysis
        - When the user asks whether a task is safe/suspicious/legitimate, call analyze_task and base
          your answer ONLY on its evidence-grounded findings. Quote the specific evidence (command,
          trigger, setting) and the risk level; never speculate beyond the returned findings.

        ## Hard rules
        - NEVER attempt to run shell/terminal commands, read or write files, browse or fetch the web,
          edit code, or use memory/extension/hook tools. These are blocked and will be rejected.
        - ONLY act on Windows scheduled tasks. If a request is unrelated (coding, web search, system
          changes, file edits, anything outside Task Scheduler), politely decline and explain you can
          only help with scheduled tasks.
        - To create a task you need a name, the program/command to run, and a schedule. Ask ONE concise
          clarifying question at a time when something essential is missing.
        - Before deleting a task (delete_task), explicitly confirm with the user first.
        - Do not invent task paths; use list_tasks/list_folders to discover them.
        - Never include secrets, passwords, or credentials in task definitions unless the user
          explicitly provides them for a "run as" account.
        - Keep responses short and focused on the scheduling task at hand.
        """;

    private void OnSessionEvent(SessionEvent evt)
    {
        switch (evt)
        {
            case AssistantMessageEvent msg when !string.IsNullOrWhiteSpace(msg.Data.Content):
                AssistantMessage?.Invoke(msg.Data.Content);
                break;
            case ToolExecutionStartEvent start:
                ToolActivity?.Invoke(DescribeTool(start));
                break;
            case SessionErrorEvent err:
                ErrorMessage?.Invoke(err.Data.Message);
                break;
        }
    }

    /// <summary>Maps the tool being invoked to a friendly, action-specific status line so the
    /// transcript reflects what's actually happening (reading vs. creating vs. analyzing, etc.)
    /// instead of always saying the same thing. Handles both in-process tool names and
    /// MCP-routed names (which may be namespaced, e.g. "mcp__tasker__list_tasks").</summary>
    private static string DescribeTool(ToolExecutionStartEvent start)
    {
        var name = (start.Data.McpToolName ?? start.Data.ToolName ?? string.Empty).ToLowerInvariant();
        if (name.Length == 0) return "Working\u2026";

        bool Has(string key) => name.Contains(key, StringComparison.Ordinal);

        if (Has("analyze_task")) return "Analyzing tasks for risks\u2026";
        if (Has("create_task")) return "Creating the scheduled task\u2026";
        if (Has("list_folders") || Has("list_tasks")) return "Reading your scheduled tasks\u2026";
        if (Has("get_task")) return "Looking up task details\u2026";
        if (Has("run_task")) return "Starting the task\u2026";
        if (Has("stop_task")) return "Stopping the task\u2026";
        if (Has("enable_task")) return "Enabling the task\u2026";
        if (Has("disable_task")) return "Disabling the task\u2026";
        if (Has("delete_task")) return "Deleting the task\u2026";
        return "Working with Windows Task Scheduler\u2026";
    }

    public async Task SendAsync(string prompt)
    {
        if (_session is null) throw new InvalidOperationException("Assistant is not connected.");
        await _sendGate.WaitAsync();
        try
        {
            var idle = new TaskCompletionSource();
            using var sub = _session.On<SessionEvent>(evt =>
            {
                if (evt is SessionIdleEvent) idle.TrySetResult();
            });
            await _session.SendAsync(new MessageOptions { Prompt = prompt });
            await idle.Task;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>Drops the current client/session so the next message reconnects (e.g., after token change).</summary>
    public async Task ResetAsync() => await DisposeSessionAsync();

    /// <summary>Lists the Copilot models available to the signed-in account, including each model's
    /// supported reasoning-effort levels. Requires the client to start (auth), so it may prompt sign-in.</summary>
    public async Task<IReadOnlyList<ModelOption>> ListModelsAsync()
    {
        if (!await EnsureStartedAsync() || _client is null)
            return Array.Empty<ModelOption>();
        try
        {
            var models = await _client.ListModelsAsync();
            return models
                .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                .Select(m => new ModelOption(
                    m.Id,
                    string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name,
                    (m.SupportedReasoningEfforts ?? new List<string>()).ToArray(),
                    m.DefaultReasoningEffort))
                .ToList();
        }
        catch (Exception ex)
        {
            ErrorMessage?.Invoke(FriendlyError(ex));
            return Array.Empty<ModelOption>();
        }
    }

    // ---------------------------------------------------------------- tools

    private async Task<string> CreateTaskAsync(
        [Description("Task name (required).")] string name,
        [Description("Full path to the program or script to run, e.g. C:\\Windows\\System32\\notepad.exe (required).")] string command,
        [Description("Command-line arguments for the program (optional).")] string? arguments = null,
        [Description("Destination folder path, e.g. \\ or \\MyApps. Created if missing. Defaults to \\.")] string? folder = null,
        [Description("Schedule type: oneTime, daily, weekly, monthly, atStartup, atLogOn, or onIdle. Defaults to daily.")] string? scheduleKind = null,
        [Description("Start date-time in ISO 8601 (e.g. 2026-06-10T09:00:00) for time-based schedules.")] string? startDateTime = null,
        [Description("For daily schedules: run every N days. Defaults to 1.")] int daysInterval = 1,
        [Description("For weekly schedules: comma-separated weekdays, e.g. Monday,Friday.")] string? daysOfWeek = null,
        [Description("Run with highest privileges (requires the app to run as administrator).")] bool runHighest = false,
        [Description("Optional task description.")] string? description = null)
    {
        if (ValidateName(name) is { } nameErr) return nameErr;
        if (string.IsNullOrWhiteSpace(command)) return Err("A program/command to run is required.");

        var kind = ParseKind(scheduleKind);
        var trigger = new TriggerDto { Kind = kind, Enabled = true };

        if (DateTime.TryParse(startDateTime, out var start)) trigger.StartBoundary = start;
        else if (kind is TriggerKind.OneTime or TriggerKind.Daily or TriggerKind.Weekly or TriggerKind.Monthly)
            trigger.StartBoundary = DateTime.Now.AddMinutes(1);

        trigger.DaysInterval = Math.Max(1, daysInterval);
        if (kind == TriggerKind.Weekly && !string.IsNullOrWhiteSpace(daysOfWeek))
        {
            foreach (var d in daysOfWeek.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                trigger.DaysOfWeek.Add(d);
        }

        var request = new TaskCreateRequest
        {
            Name = name.Trim(),
            Folder = NormalizeFolder(folder),
            Description = description ?? string.Empty,
            Principal = new PrincipalDto { RunWithHighestPrivileges = runHighest, LogonType = "InteractiveToken" },
            Actions = { new ActionDto { Kind = ActionKind.Exec, Command = command.Trim(), Arguments = arguments ?? string.Empty } },
            Triggers = { trigger },
        };

        var result = await TaskerClient.CreateOrUpdateAsync(request);
        if (result.Success) { LastCreatedTaskPath = result.Path; AppEvents.RaiseTasksChanged(); }
        return JsonSerializer.Serialize(result, JsonOpts);
    }

    private async Task<string> CreateTaskFromXmlAsync(
        [Description("Task name (required).")] string name,
        [Description("Raw Task Scheduler XML (required).")] string xml,
        [Description("Destination folder path. Defaults to \\.")] string? folder = null)
    {
        if (ValidateName(name) is { } nameErr) return nameErr;
        if (string.IsNullOrWhiteSpace(xml)) return Err("Task XML is required.");

        var result = await TaskerClient.ImportXmlAsync(NormalizeFolder(folder), name.Trim(), xml);
        if (result.Success) { LastCreatedTaskPath = result.Path; AppEvents.RaiseTasksChanged(); }
        return JsonSerializer.Serialize(result, JsonOpts);
    }

    private static async Task<string> ListFoldersAsync()
    {
        var paths = await TaskerClient.GetFolderPathsAsync();
        return JsonSerializer.Serialize(paths, JsonOpts);
    }

    private static async Task<string> ListTasksAsync(
        [Description("Folder path to list, e.g. \\ or \\MyApps. Defaults to \\.")] string? folder = null,
        [Description("Include subfolders. Defaults to false.")] bool recursive = false)
    {
        var tasks = await TaskerClient.ListTasksAsync(NormalizeFolder(folder), recursive);
        var slim = tasks.Select(t => new { t.Name, t.Path, t.StateText, t.TriggersSummary, t.ActionsSummary });
        return JsonSerializer.Serialize(slim, JsonOpts);
    }

    private static async Task<string> GetTaskAsync(
        [Description("Full task path, e.g. \\MyApps\\Backup.")] string path)
    {
        var detail = await TaskerClient.GetTaskAsync(path);
        return detail is null ? Err($"Task not found: {path}") : JsonSerializer.Serialize(detail, JsonOpts);
    }

    private static async Task<string> AnalyzeTaskAsync(
        [Description("Full task path to analyze, e.g. \\Folder\\MyTask.")] string path)
    {
        var analysis = await TaskerClient.AnalyzeTaskAsync(path);
        return JsonSerializer.Serialize(analysis, JsonOpts);
    }

    private async Task<string> RunTaskAsync([Description("Full task path.")] string path)
        => JsonSerializer.Serialize(await TaskerClient.RunTaskAsync(path), JsonOpts);

    private async Task<string> StopTaskAsync([Description("Full task path.")] string path)
        => JsonSerializer.Serialize(await TaskerClient.StopTaskAsync(path), JsonOpts);

    private async Task<string> EnableTaskAsync([Description("Full task path.")] string path)
    {
        var r = await TaskerClient.SetEnabledAsync(path, true);
        if (r.Success) AppEvents.RaiseTasksChanged();
        return JsonSerializer.Serialize(r, JsonOpts);
    }

    private async Task<string> DisableTaskAsync([Description("Full task path.")] string path)
    {
        var r = await TaskerClient.SetEnabledAsync(path, false);
        if (r.Success) AppEvents.RaiseTasksChanged();
        return JsonSerializer.Serialize(r, JsonOpts);
    }

    private async Task<string> DeleteTaskAsync([Description("Full task path.")] string path)
    {
        var r = await TaskerClient.DeleteTaskAsync(path);
        if (r.Success) AppEvents.RaiseTasksChanged();
        return JsonSerializer.Serialize(r, JsonOpts);
    }

    private static string Err(string message) => JsonSerializer.Serialize(new { success = false, message }, JsonOpts);

    // ---------------------------------------------------------------- helpers

    private static TriggerKind ParseKind(string? kind) =>
        Enum.TryParse<TriggerKind>(kind, true, out var k) ? k : TriggerKind.Daily;

    /// <summary>Returns an error-result JSON string if the name is unsafe, otherwise null.</summary>
    private static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Err("A task name is required.");
        if (name.Length > 240) return Err("Task name is too long.");
        if (name.Any(c => char.IsControl(c)) || name.Contains('\\') || name.Contains('/'))
            return Err("Task name contains invalid characters (no control characters, '\\' or '/').");
        return null;
    }

    private static string NormalizeFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "\\";
        folder = folder.Trim();
        return folder.StartsWith('\\') ? folder : "\\" + folder;
    }

    private static string BuildSystemPrompt() => $$"""
        You are the Windows Tasker assistant, embedded inside a Windows Task Scheduler replacement
        app. You help the user create, inspect, and manage Windows scheduled tasks using your tools:
        create_task, create_task_from_xml, list_folders, list_tasks, get_task, run_task, stop_task,
        enable_task, disable_task, and delete_task.

        Rules:
        - You are sandboxed: ONLY the Windows Tasker task tools work. Shell, file, web, and memory
          tools are blocked, so never attempt them.
        - To CREATE a task, gather a name, the exact program/command to run, and when it should run.
          If any is missing, ask ONE short clarifying question and wait. Then briefly restate the plan
          and call create_task (do not ask for confirmation more than once).
        - To ANSWER questions ("what runs at startup?", "is X enabled?"), call list_tasks (with
          recursive=true when needed) or get_task and summarize concisely.
        - To JUDGE whether a task is safe or suspicious, call analyze_task and report its risk level
          and evidence-grounded findings (quote the specific command/trigger/setting); don't speculate.
        - To MANAGE tasks (run/stop/enable/disable), call the matching tool with the task's path; look
          it up with list_tasks first if you only have a name.
        - Before delete_task, briefly confirm with the user.
        - Prefer absolute program paths (e.g. C:\Windows\System32\notepad.exe).
        - After an action, report the result in one short sentence.
        - Politely decline anything unrelated to Windows scheduled tasks.
        - Today's date is {{DateTime.Now:yyyy-MM-dd}}. Keep replies concise and friendly.
        """;

    private static string FriendlyError(Exception ex)
    {
        var m = ex.Message;
        if (m.Contains("auth", StringComparison.OrdinalIgnoreCase) || m.Contains("token", StringComparison.OrdinalIgnoreCase)
            || m.Contains("401") || m.Contains("unauthor", StringComparison.OrdinalIgnoreCase))
            return "Couldn't authenticate with GitHub Copilot. Add a GitHub token (with Copilot access) below, or sign in with the Copilot CLI, then try again.";
        return $"Couldn't reach GitHub Copilot: {m}";
    }

    private async Task DisposeSessionAsync()
    {
        try { if (_session is not null) await _session.DisposeAsync(); } catch { }
        try { if (_client is not null) await _client.StopAsync(); } catch { }
        _session = null;
        _client = null;
    }

    public async ValueTask DisposeAsync() => await DisposeSessionAsync();
}

/// <summary>A selectable Copilot model and the reasoning-effort levels it supports.</summary>
/// <param name="Id">Model id passed to the session (e.g. "auto", "gpt-5").</param>
/// <param name="Name">Friendly display name.</param>
/// <param name="Efforts">Supported reasoning-effort values; empty when the model has none.</param>
/// <param name="DefaultEffort">The model's default reasoning effort, if any.</param>
public sealed record ModelOption(string Id, string Name, IReadOnlyList<string> Efforts, string? DefaultEffort);
