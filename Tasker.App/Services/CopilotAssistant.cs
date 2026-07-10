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
    private const string VaultResource = "WinTaskScheduler.Copilot";
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
    /// installed; unaffected by version/install-location changes. Used only as a fallback if
    /// staging (see <see cref="EnsureStagedMcpServer"/>) can't run for some reason.</summary>
    public const string McpAlias = "WinTaskSchedulerMcp.exe";

    private static bool IsPackaged => TryPackagePath() is not null;

    /// <summary>Whether this process has package identity (installed/MSIX run vs. an unpackaged dev
    /// build). Exposed for diagnostics only, e.g. tailoring error messages so Store users never see
    /// dev-oriented advice like "build the solution".</summary>
    public static bool IsPackagedForDiagnostics => IsPackaged;

    /// <summary>The command external MCP clients (and the in-app MCP routing) should launch to start
    /// the bundled server. For an installed/packaged app this is a per-user staged copy (see
    /// <see cref="EnsureStagedMcpServer"/>), so external tools never have to reach into the
    /// protected WindowsApps folder; for an unpackaged dev run it's the absolute path to the built
    /// server exe.</summary>
    public static string McpServerCommand => IsPackaged ? EnsureStagedMcpServer() : McpExePath;

    private static string? TryPackagePath()
    {
        try { return Windows.ApplicationModel.Package.Current.InstalledLocation.Path; }
        catch { return null; }
    }

    public static bool McpAvailable => System.IO.File.Exists(McpExePath);

    /// <summary>Per-user staging folder for the bundled MCP server. The packaged app's real install
    /// location under WindowsApps is protected: Windows Explorer won't browse it, and other
    /// processes/users can't reliably reach it directly, even though the app-execution alias
    /// usually resolves it fine. Staging a plain copy here removes that dependency entirely, so
    /// external MCP clients (Copilot CLI, VS Code, Claude Code) always get a normal, unrestricted
    /// path regardless of install method, elevation, or alias registration quirks.
    ///
    /// TRUE ROOT CAUSE of the "staged path doesn't exist" bug: for a full-trust MSIX-packaged app,
    /// Windows transparently redirects classic Win32 file I/O against
    /// <c>Environment.SpecialFolder.LocalApplicationData</c> (what <c>%LOCALAPPDATA%</c> naively
    /// looks like, e.g. <c>C:\Users\me\AppData\Local</c>) to a package-private compatibility folder
    /// under <c>...\AppData\Local\Packages\&lt;PackageFamilyName&gt;\LocalCache\Local\...</c>. The
    /// app was always staging successfully — just into that redirected location — so the plain
    /// <c>%LOCALAPPDATA%\WindowsTasker\mcp</c> path written into external MCP client configs never
    /// actually existed there; only a non-packaged process (or the app itself, since it's
    /// consistently redirected) would ever see it. External tools got a confidently-reported but
    /// nonexistent path. <see cref="Windows.Storage.ApplicationData.LocalCacheFolder"/> reports the
    /// real, unredirected path Windows actually uses, so using it here keeps the app and any
    /// external tool looking at the exact same real file.</summary>
    private static string StagedMcpDir => System.IO.Path.Combine(
        IsPackaged
            ? Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowsTasker", "mcp");

    private static string StagedMcpExePath => System.IO.Path.Combine(StagedMcpDir, "Tasker.Mcp.exe");

    /// <summary>Serializes staging attempts. <see cref="WarmMcpServerStaging"/> kicks off a
    /// background copy at startup; without this lock, a near-simultaneous synchronous call to <see
    /// cref="McpServerCommand"/> (e.g. opening Settings or clicking an Install button right after
    /// launch) could race it and hit a file-sharing violation mid-copy, which the old code treated
    /// as fatal and fell back to the app-execution alias instead of the robust staged path.</summary>
    private static readonly object StagingLock = new();

    /// <summary>Reason the last staging attempt fell back to the alias, or null if the most recent
    /// attempt produced (or already had) a working staged copy. Lets the UI tell users on machines
    /// where staging silently never worked (see <see cref="McpServerCommand"/>) what actually went
    /// wrong, instead of just handing them a less-robust alias with no explanation.</summary>
    public static string? LastStagingError { get; private set; }

    /// <summary>Copies the bundled MCP server from the package's install folder into <see
    /// cref="StagedMcpDir"/> the first time it's needed, and again whenever the bundled copy is
    /// newer or a different size (e.g. after an app update). Cheap to call repeatedly: skips the
    /// copy entirely once the staged copy is already current.
    ///
    /// Confirmed root cause of one real failure mode: <see cref="WarmMcpServerStaging"/>'s
    /// background copy at startup and a near-simultaneous synchronous call from the UI (e.g.
    /// clicking an Install button right after launch) used to be able to race each other mid-copy
    /// of the same destination files, throwing a file-sharing violation; that was treated as fatal
    /// and silently fell back to the app-execution alias instead of the robust staged path, even
    /// when a perfectly good staged copy already existed. <see cref="StagingLock"/> serializes
    /// callers so that can't happen, and as defense in depth, any refresh failure here still
    /// prefers an existing staged copy over the alias.
    ///
    /// Also guards against a silent-failure mode seen on at least one machine where nothing was
    /// ever staged at all (first run, no prior copy to fall back to): enumerating the source
    /// folder can come back empty on some machines/policies without throwing, which used to make
    /// this method report success (returning a path to a .exe that was never actually written).
    /// The explicit existence check after copying turns that into a proper failure so it falls back
    /// to the alias instead of handing out a broken path. <see cref="LastStagingError"/> captures
    /// the reason so it can be surfaced in the UI instead of failing silently.</summary>
    private static string EnsureStagedMcpServer()
    {
        lock (StagingLock)
        {
            var stagedExe = StagedMcpExePath;
            try
            {
                var sourceExe = McpExePath;
                if (!System.IO.File.Exists(sourceExe))
                {
                    if (System.IO.File.Exists(stagedExe)) return stagedExe;
                    LastStagingError = $"Bundled server not found at '{sourceExe}'.";
                    return McpAlias;
                }

                var sourceDir = System.IO.Path.GetDirectoryName(sourceExe)!;

                var upToDate = System.IO.File.Exists(stagedExe)
                    && System.IO.File.GetLastWriteTimeUtc(sourceExe) == System.IO.File.GetLastWriteTimeUtc(stagedExe)
                    && new System.IO.FileInfo(sourceExe).Length == new System.IO.FileInfo(stagedExe).Length;

                if (!upToDate)
                {
                    System.IO.Directory.CreateDirectory(StagedMcpDir);
                    var copied = 0;
                    foreach (var file in System.IO.Directory.EnumerateFiles(sourceDir, "*", System.IO.SearchOption.AllDirectories))
                    {
                        var rel = System.IO.Path.GetRelativePath(sourceDir, file);
                        var dest = System.IO.Path.Combine(StagedMcpDir, rel);
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
                        System.IO.File.Copy(file, dest, overwrite: true);
                        System.IO.File.SetLastWriteTimeUtc(dest, System.IO.File.GetLastWriteTimeUtc(file));
                        copied++;
                    }

                    // Enumerating the source can silently yield zero files on some machines/policies
                    // (no exception, just nothing to iterate) — don't report success in that case.
                    if (copied == 0 && !System.IO.File.Exists(stagedExe))
                        throw new System.IO.IOException($"Enumerating '{sourceDir}' produced no files to stage (possibly blocked directory listing).");
                }

                if (!System.IO.File.Exists(stagedExe))
                    throw new System.IO.IOException($"Staging finished without producing '{stagedExe}'.");

                LastStagingError = null;
                return stagedExe;
            }
            catch (Exception ex)
            {
                // A refresh attempt failed (e.g. transient access issue reading from WindowsApps, or
                // a lock held by an in-flight MCP server process). Prefer a previously-staged copy —
                // it's still a normal, directly-reachable file — over the alias, which depends on
                // PATH/alias-registration quirks the staged copy exists to avoid.
                LastStagingError = ex.Message;
                return System.IO.File.Exists(stagedExe) ? stagedExe : McpAlias;
            }
        }
    }

    /// <summary>Best-effort warm-up so the staged copy (see <see cref="EnsureStagedMcpServer"/>) is
    /// already current by the time the user opens Settings or an external tool tries to launch it,
    /// instead of only staging lazily on first read of <see cref="McpServerCommand"/>. Runs on a
    /// background thread and never throws; safe to call from the UI thread at startup.</summary>
    public static void WarmMcpServerStaging()
    {
        if (!IsPackaged) return;
        _ = Task.Run(() => { try { EnsureStagedMcpServer(); } catch { /* best-effort */ } });
    }

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

            _client = new CopilotClient(options);
            await _client.StartAsync();

            var config = new SessionConfig
            {
                Model = SelectedModel,
                WorkingDirectory = workspace,
                // Hard safety boundary: only the Task Scheduler Studio task tools may execute. Every other
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

            // The assistant always runs its task tools in-process (reliable, no extra process or
            // token needed). External agents use the bundled MCP server instead — see McpInstaller.
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

    /// <summary>Approves only the Task Scheduler Studio task tools; rejects shell, file, web, memory, and
    /// every other capability — the hard safety boundary for the embedded agent.</summary>
    private static Task<PermissionDecision> HandlePermissionAsync(PermissionRequest request, PermissionInvocation invocation)
    {
        PermissionDecision decision = request switch
        {
            PermissionRequestMcp mcp when string.Equals(mcp.ServerName, "wintask-scheduler", StringComparison.OrdinalIgnoreCase)
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
            "WinTaskScheduler", "agent");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "AGENTS.md"), AgentInstructions);
        return dir;
    }

    private const string AgentInstructions = """
        # Task Scheduler Studio Agent

        You are an assistant embedded in the **Task Scheduler Studio** desktop app. Your sole purpose is to
        help the user **create, inspect, and manage Windows Scheduled Tasks** through the provided
        Task Scheduler Studio tools.

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
        You are the Task Scheduler Studio assistant, embedded inside a Windows Task Scheduler replacement
        app. You help the user create, inspect, and manage Windows scheduled tasks using your tools:
        create_task, create_task_from_xml, list_folders, list_tasks, get_task, run_task, stop_task,
        enable_task, disable_task, and delete_task.

        Rules:
        - You are sandboxed: ONLY the Task Scheduler Studio task tools work. Shell, file, web, and memory
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
