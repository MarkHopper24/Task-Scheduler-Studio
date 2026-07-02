using System.Text.Json.Nodes;

namespace Tasker.Mcp;

/// <summary>Builds the MCP tool definitions (names, descriptions, JSON-Schema input shapes).</summary>
internal static class ToolCatalog
{
    public static JsonArray Build()
    {
        return new JsonArray
        {
            Tool("list_folders",
                "List the Task Scheduler folder tree (paths and task counts), rooted at '\\'.",
                Obj(props: new JsonObject(), required: null)),

            Tool("list_tasks",
                "List scheduled tasks in a folder with their state, next/last run time, last result, "
                + "and human-readable trigger and action summaries.",
                Obj(new JsonObject
                {
                    ["folder"] = Prop("string", "Folder path, e.g. '\\' (root) or '\\MyApps'. Defaults to '\\'."),
                    ["recursive"] = Prop("boolean", "Recurse into subfolders. Defaults to false."),
                    ["filter"] = Prop("string", "Case-insensitive substring filter over name, path, action, description."),
                })),

            Tool("get_task",
                "Get the full definition of one task: principal, settings, triggers, actions, run state, "
                + "last/next run, and the raw Task Scheduler XML.",
                Obj(new JsonObject
                {
                    ["path"] = Prop("string", "Full task path, e.g. '\\MyTask' or '\\Folder\\MyTask'."),
                }, "path")),

            Tool("export_task_xml",
                "Return the portable Task Scheduler XML for a task (the interchange format the built-in "
                + "app uses for Import/Export).",
                Obj(new JsonObject
                {
                    ["path"] = Prop("string", "Full task path."),
                }, "path")),

            Tool("analyze_task",
                "Parse a scheduled task and return a grounded, evidence-based assessment of it: a risk "
                + "level (Low/Medium/High), a list of findings that each cite the exact command, "
                + "trigger, principal or setting they are based on, and the observed facts. Useful for "
                + "deciding whether a task looks legitimate or suspicious. The verdict is derived only "
                + "from the task's stored definition, not file signatures or runtime behaviour.",
                Obj(new JsonObject
                {
                    ["path"] = Prop("string", "Full task path, e.g. '\\Folder\\MyTask'."),
                }, "path")),

            Tool("run_task", "Start a task immediately (on demand).",
                Obj(new JsonObject { ["path"] = Prop("string", "Full task path.") }, "path")),

            Tool("stop_task", "Stop all running instances of a task.",
                Obj(new JsonObject { ["path"] = Prop("string", "Full task path.") }, "path")),

            Tool("enable_task", "Enable a task.",
                Obj(new JsonObject { ["path"] = Prop("string", "Full task path.") }, "path")),

            Tool("disable_task", "Disable a task (keeps the definition, prevents it from running).",
                Obj(new JsonObject { ["path"] = Prop("string", "Full task path.") }, "path")),

            Tool("delete_task", "Permanently delete a task.",
                Obj(new JsonObject { ["path"] = Prop("string", "Full task path.") }, "path")),

            Tool("get_running_tasks",
                "List tasks that are currently running, with their current action and engine PID.",
                Obj(props: new JsonObject(), required: null)),

            Tool("create_task",
                "Create or update a scheduled task. Provide EITHER 'xml' (raw Task Scheduler XML, full "
                + "fidelity) OR structured 'triggers' + 'actions'. If a task with the same folder+name "
                + "exists it is updated. The task is written to the shared Windows store.",
                CreateTaskSchema()),
        };
    }

    private static JsonObject CreateTaskSchema()
    {
        var triggerSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["kind"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("oneTime", "daily", "weekly", "monthly", "monthlyDOW", "atStartup", "atLogOn", "onIdle", "onEvent", "onSessionStateChange"),
                    ["description"] = "Trigger type.",
                },
                ["enabled"] = Prop("boolean", "Defaults to true."),
                ["startBoundary"] = Prop("string", "ISO-8601 local date-time, e.g. '2026-01-01T09:00:00'. Used by time-based kinds."),
                ["endBoundary"] = Prop("string", "Optional ISO-8601 expiry date-time."),
                ["daysInterval"] = Prop("integer", "For 'daily': run every N days."),
                ["weeksInterval"] = Prop("integer", "For 'weekly': run every N weeks."),
                ["daysOfWeek"] = ArrProp("string", "For 'weekly'/'monthlyDOW': e.g. ['Monday','Friday']."),
                ["daysOfMonth"] = ArrProp("integer", "For 'monthly': e.g. [1,15]."),
                ["monthsOfYear"] = ArrProp("string", "For 'monthly'/'monthlyDOW': e.g. ['January','July']."),
                ["weeksOfMonth"] = ArrProp("string", "For 'monthlyDOW': e.g. ['FirstWeek','LastWeek']."),
                ["stateChange"] = Prop("string", "For 'onSessionStateChange': ConsoleConnect | ConsoleDisconnect | RemoteConnect | RemoteDisconnect | SessionLock | SessionUnlock."),
                ["userId"] = Prop("string", "For 'atLogOn'/'onSessionStateChange': restrict to a specific user (optional)."),
                ["delay"] = Prop("string", "ISO-8601 duration delay for 'atStartup'/'atLogOn', e.g. 'PT5M'."),
                ["subscription"] = Prop("string", "For 'onEvent': the event XPath subscription XML."),
                ["repetitionInterval"] = Prop("string", "ISO-8601 duration to repeat, e.g. 'PT1H'."),
                ["repetitionDuration"] = Prop("string", "ISO-8601 duration for how long to keep repeating."),
            },
            ["required"] = new JsonArray("kind"),
        };

        var actionSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["command"] = Prop("string", "Program/script to run, e.g. 'C:\\\\Windows\\\\System32\\\\notepad.exe'."),
                ["arguments"] = Prop("string", "Command-line arguments (optional)."),
                ["workingDirectory"] = Prop("string", "Working directory (optional)."),
            },
            ["required"] = new JsonArray("command"),
        };

        var principalSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["userId"] = Prop("string", "Account to run as (optional; defaults to current user)."),
                ["logonType"] = Prop("string", "InteractiveToken | Password | S4U | ServiceAccount. Defaults to InteractiveToken."),
                ["runWithHighestPrivileges"] = Prop("boolean", "Run elevated. Defaults to false."),
            },
        };

        var settingsSchema = new JsonObject
        {
            ["type"] = "object",
            ["description"] = "Optional behaviour switches; sensible defaults are applied when omitted.",
            ["properties"] = new JsonObject
            {
                ["enabled"] = Prop("boolean", null),
                ["hidden"] = Prop("boolean", null),
                ["allowDemandStart"] = Prop("boolean", null),
                ["startWhenAvailable"] = Prop("boolean", "Run as soon as possible after a missed start."),
                ["runOnlyIfNetworkAvailable"] = Prop("boolean", null),
                ["disallowStartIfOnBatteries"] = Prop("boolean", null),
                ["stopIfGoingOnBatteries"] = Prop("boolean", null),
                ["wakeToRun"] = Prop("boolean", null),
                ["runOnlyIfIdle"] = Prop("boolean", null),
                ["restartOnFailure"] = Prop("boolean", null),
                ["restartCount"] = Prop("integer", null),
                ["restartInterval"] = Prop("string", "ISO-8601 duration, e.g. 'PT1M'."),
                ["executionTimeLimit"] = Prop("string", "ISO-8601 duration, e.g. 'PT72H'. Empty = no limit."),
                ["multipleInstances"] = Prop("string", "IgnoreNew | Parallel | Queue | StopExisting."),
            },
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = Prop("string", "Task name (required)."),
                ["folder"] = Prop("string", "Destination folder, e.g. '\\' or '\\MyApps'. Created if missing. Defaults to '\\'."),
                ["author"] = Prop("string", "Author (optional)."),
                ["description"] = Prop("string", "Description (optional)."),
                ["xml"] = Prop("string", "Raw Task Scheduler XML. When provided, triggers/actions/settings are ignored."),
                ["principal"] = principalSchema,
                ["settings"] = settingsSchema,
                ["triggers"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "When to run. At least one recommended (a task with no triggers only runs on demand).",
                    ["items"] = triggerSchema,
                },
                ["actions"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "What to run. Required unless 'xml' is provided.",
                    ["items"] = actionSchema,
                },
                ["userId"] = Prop("string", "Account to register under for stored-credential tasks (optional)."),
                ["password"] = Prop("string", "Password for 'userId' when using Password logon (optional)."),
            },
            ["required"] = new JsonArray("name"),
        };
    }

    // ---------------------------------------------------------------- builders

    private static JsonObject Tool(string name, string description, JsonObject inputSchema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = inputSchema,
    };

    private static JsonObject Obj(JsonObject props, string? required = null)
    {
        var o = new JsonObject { ["type"] = "object", ["properties"] = props };
        if (required is not null) o["required"] = new JsonArray(required);
        return o;
    }

    private static JsonObject Prop(string type, string? description)
    {
        var o = new JsonObject { ["type"] = type };
        if (description is not null) o["description"] = description;
        return o;
    }

    private static JsonObject ArrProp(string itemType, string description) => new()
    {
        ["type"] = "array",
        ["description"] = description,
        ["items"] = new JsonObject { ["type"] = itemType },
    };
}
