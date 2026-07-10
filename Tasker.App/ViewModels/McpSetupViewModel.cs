using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Tasker.Core;
using Tasker_App.Services;

namespace Tasker_App.ViewModels;

public partial class McpSetupViewModel : ObservableObject
{
    public string McpServerCommand => McpInstaller.McpServerCommand;
    public string CopilotCliConfigPath => McpInstaller.CopilotCliConfigPath;
    public string VSCodeConfigPath => McpInstaller.VSCodeConfigPath;
    public string ClaudeCodeConfigPath => McpInstaller.ClaudeCodeConfigPath;

    public string CopilotCliSnippet => BuildSnippet("mcpServers");
    public string VSCodeSnippet => BuildSnippet("servers");
    public string ClaudeSnippet => BuildSnippet("mcpServers");

    [ObservableProperty]
    public partial bool InstallOpen { get; set; }

    [ObservableProperty]
    public partial string InstallStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity InstallSeverity { get; set; } = InfoBarSeverity.Success;

    [RelayCommand]
    private void InstallCopilot() => ReportInstall(McpInstaller.InstallForCopilotCli());

    [RelayCommand]
    private void InstallVSCode() => ReportInstall(McpInstaller.InstallForVSCode());

    [RelayCommand]
    private void InstallClaude() => ReportInstall(McpInstaller.InstallForClaudeCode());

    private void ReportInstall(OperationResult result)
    {
        InstallStatus = result.Message;
        InstallSeverity = result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        InstallOpen = true;
    }

    private static string BuildSnippet(string collectionKey)
    {
        var command = McpInstaller.McpServerCommand.Replace("\\", "\\\\");
        return
            "{\n" +
            "  \"" + collectionKey + "\": {\n" +
            "    \"wintask-scheduler\": {\n" +
            "      \"type\": \"stdio\",\n" +
            "      \"command\": \"" + command + "\",\n" +
            "      \"args\": []\n" +
            "    }\n" +
            "  }\n" +
            "}";
    }
}
