using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tasker.Core;

namespace Tasker_App.Services;

/// <summary>
/// Installs the bundled WinTask Scheduler MCP server into the GitHub Copilot CLI and VS Code MCP
/// configurations by merging an entry into the right config file (preserving any existing servers).
/// </summary>
public static class McpInstaller
{
    public const string ServerName = "wintask-scheduler";

    public static string McpExePath => CopilotAssistant.McpExePath;
    public static bool McpAvailable => File.Exists(McpExePath);

    /// <summary>The command written into external MCP client configs: a stable app-execution alias
    /// when installed, or the absolute server path for unpackaged dev runs.</summary>
    public static string McpServerCommand => CopilotAssistant.McpServerCommand;

    public static string CopilotCliConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "mcp-config.json");

    public static string VSCodeConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User", "mcp.json");

    public static string ClaudeCodeConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Merges the server into <c>~/.copilot/mcp-config.json</c> under <c>mcpServers</c>.</summary>
    public static OperationResult InstallForCopilotCli()
    {
        return Install(CopilotCliConfigPath, "mcpServers", new JsonObject
        {
            ["name"] = ServerName,
            ["type"] = "stdio",
            ["command"] = McpServerCommand,
            ["args"] = new JsonArray(),
            ["env"] = new JsonObject(),
            ["tools"] = new JsonArray(),
            ["enabled"] = true,
        }, "GitHub Copilot CLI");
    }

    /// <summary>Merges the server into <c>%APPDATA%/Code/User/mcp.json</c> under <c>servers</c>.</summary>
    public static OperationResult InstallForVSCode()
    {
        return Install(VSCodeConfigPath, "servers", new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = McpServerCommand,
            ["args"] = new JsonArray(),
        }, "VS Code");
    }

    /// <summary>Merges the server into the user-scoped Claude Code config (<c>~/.claude.json</c>)
    /// under <c>mcpServers</c>, so it's available across all Claude Code projects.</summary>
    public static OperationResult InstallForClaudeCode()
    {
        return Install(ClaudeCodeConfigPath, "mcpServers", new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = McpServerCommand,
            ["args"] = new JsonArray(),
            ["env"] = new JsonObject(),
        }, "Claude Code");
    }

    private static OperationResult Install(string configPath, string collectionKey, JsonObject serverEntry, string target)
    {
        try
        {
            if (!McpAvailable)
                return OperationResult.Fail($"The bundled MCP server wasn't found at '{McpExePath}'. Build the solution first.");

            JsonObject root;
            if (File.Exists(configPath))
            {
                var text = File.ReadAllText(configPath);
                if (string.IsNullOrWhiteSpace(text))
                {
                    root = new JsonObject();
                }
                else if (JsonNode.Parse(text, documentOptions: ParseOptions) is JsonObject parsed)
                {
                    root = parsed;
                }
                else
                {
                    return OperationResult.Fail($"Existing {target} config isn't a JSON object; add the server manually to keep your settings safe.");
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                root = new JsonObject();
            }

            if (root[collectionKey] is not JsonObject servers)
            {
                servers = new JsonObject();
                root[collectionKey] = servers;
            }

            var existed = servers[ServerName] is not null;
            servers[ServerName] = serverEntry;

            File.WriteAllText(configPath, root.ToJsonString(WriteOptions));
            var verb = existed ? "Updated" : "Installed";
            return OperationResult.Ok($"{verb} '{ServerName}' for {target}. Restart {target} to pick it up. ({configPath})", configPath);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Couldn't write the {target} config: {ex.Message}");
        }
    }
}
