using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Tasker.Core;

namespace Tasker.Mcp;

/// <summary>
/// A local stdio MCP (Model Context Protocol) server exposing the Windows Task Scheduler to
/// agents. Speaks newline-delimited JSON-RPC 2.0 over stdin/stdout (the MCP stdio transport),
/// and delegates every operation to the shared <see cref="TaskerService"/> — the exact same
/// COM-backed layer the WinUI app uses, so changes are immediately visible in both apps and in
/// the built-in Task Scheduler.
///
/// Diagnostics go to stderr only; stdout is reserved for protocol traffic.
/// </summary>
internal static class Program
{
    private const string ProtocolVersion = "2024-11-05";
    private const string ServerName = "wintask-scheduler";
    private const string ServerVersion = "1.0.0";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static TaskerService _service = null!;

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            _service = new TaskerService();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[wintask-scheduler] failed to connect to Task Scheduler: {ex.Message}");
            return 1;
        }

        var stdin = Console.In;
        string? line;
        while ((line = stdin.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonNode? request;
            try { request = JsonNode.Parse(line); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[wintask-scheduler] bad JSON: {ex.Message}");
                continue;
            }
            if (request is null) continue;

            try { HandleMessage(request); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[wintask-scheduler] handler error: {ex}");
                var id = request["id"];
                if (id is not null)
                    WriteError(id.DeepClone(), -32603, ex.Message);
            }
        }
        return 0;
    }

    private static void HandleMessage(JsonNode request)
    {
        var method = request["method"]?.GetValue<string>();
        var id = request["id"]?.DeepClone();
        if (method is null) return;

        switch (method)
        {
            case "initialize":
                WriteResult(id, new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = ServerName,
                        ["version"] = ServerVersion,
                    },
                    ["instructions"] = "Manage Windows Scheduled Tasks. Tasks are stored in the live "
                        + "Windows Task Scheduler store and are shared with the built-in app. Use "
                        + "list_tasks/get_task to inspect, run_task/stop_task/enable_task/disable_task "
                        + "to control, create_task (structured or raw XML) to author, and "
                        + "export_task_xml for the portable interchange format.",
                });
                break;

            case "notifications/initialized":
            case "notifications/cancelled":
                // notifications carry no id; nothing to reply
                break;

            case "ping":
                WriteResult(id, new JsonObject());
                break;

            case "tools/list":
                WriteResult(id, new JsonObject { ["tools"] = ToolCatalog.Build() });
                break;

            case "tools/call":
                HandleToolCall(id, request["params"]);
                break;

            default:
                if (id is not null) WriteError(id, -32601, $"Method not found: {method}");
                break;
        }
    }

    private static void HandleToolCall(JsonNode? id, JsonNode? prms)
    {
        var name = prms?["name"]?.GetValue<string>();
        var args = prms?["arguments"];
        if (string.IsNullOrEmpty(name))
        {
            WriteError(id, -32602, "Missing tool name.");
            return;
        }

        try
        {
            var (payload, isError) = Dispatch(name!, args);
            WriteResult(id, new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = payload,
                }),
                ["isError"] = isError,
            });
        }
        catch (Exception ex)
        {
            WriteResult(id, new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = $"Error: {ex.Message}",
                }),
                ["isError"] = true,
            });
        }
    }

    private static (string payload, bool isError) Dispatch(string tool, JsonNode? args)
    {
        string Str(string key) => args?[key]?.GetValue<string>() ?? string.Empty;
        bool Bool(string key, bool dflt = false) =>
            args?[key] is JsonNode n && n.GetValueKind() == JsonValueKind.True ? true
            : args?[key] is JsonNode f && f.GetValueKind() == JsonValueKind.False ? false : dflt;

        switch (tool)
        {
            case "list_folders":
                return (Serialize(_service.GetFolderTree()), false);

            case "list_tasks":
            {
                var folder = string.IsNullOrEmpty(Str("folder")) ? "\\" : Str("folder");
                var tasks = _service.ListTasks(folder, Bool("recursive"), Str("filter"));
                return (Serialize(tasks), false);
            }

            case "get_running_tasks":
                return (Serialize(_service.GetRunningTasks()), false);

            case "get_task":
            {
                var detail = _service.GetTask(RequirePath(args));
                return detail is null
                    ? ($"Task not found: {RequirePath(args)}", true)
                    : (Serialize(detail), false);
            }

            case "export_task_xml":
                return (_service.ExportXml(RequirePath(args)), false);

            case "analyze_task":
            {
                var analysis = _service.AnalyzeTask(RequirePath(args));
                return (Serialize(analysis), !analysis.Found);
            }

            case "run_task":      return Result(_service.RunTask(RequirePath(args)));
            case "stop_task":     return Result(_service.StopTask(RequirePath(args)));
            case "enable_task":   return Result(_service.SetEnabled(RequirePath(args), true));
            case "disable_task":  return Result(_service.SetEnabled(RequirePath(args), false));
            case "delete_task":   return Result(_service.DeleteTask(RequirePath(args)));

            case "create_task":
            {
                if (args is null) return ("Missing arguments.", true);
                var req = args.Deserialize<TaskCreateRequest>(JsonOpts)
                          ?? throw new ArgumentException("Invalid create_task arguments.");
                return Result(_service.CreateOrUpdate(req));
            }

            default:
                return ($"Unknown tool: {tool}", true);
        }
    }

    private static string RequirePath(JsonNode? args)
    {
        var path = args?["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("The 'path' argument is required (e.g. \\\\MyTask or \\\\Folder\\\\MyTask).");
        return path!;
    }

    private static (string, bool) Result(OperationResult r) => (Serialize(r), !r.Success);

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOpts);

    // ---------------------------------------------------------------- transport

    private static void WriteResult(JsonNode? id, JsonNode result)
    {
        if (id is null) return; // response only for requests
        Send(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result,
        });
    }

    private static void WriteError(JsonNode? id, int code, string message)
    {
        Send(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        });
    }

    private static readonly object _writeLock = new();

    private static void Send(JsonObject message)
    {
        var json = message.ToJsonString();
        lock (_writeLock)
        {
            Console.Out.Write(json);
            Console.Out.Write('\n');
            Console.Out.Flush();
        }
    }
}
