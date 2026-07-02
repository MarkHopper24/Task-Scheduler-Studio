using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tasker_App.Services;

namespace Tasker_App.Views;

public partial class AboutPageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string ConnectionText { get; set; } = "Connecting to Task Scheduler\u2026";

    public string McpExePath => McpInstaller.McpExePath;
    public string McpServerCommand => McpInstaller.McpServerCommand;

    // Config file locations for the manual-setup instructions.
    public string CopilotCliConfigPath => McpInstaller.CopilotCliConfigPath;
    public string VSCodeConfigPath => McpInstaller.VSCodeConfigPath;
    public string ClaudeCodeConfigPath => McpInstaller.ClaudeCodeConfigPath;

    // Ready-to-paste config snippets (command escaped for JSON). Copilot CLI and Claude Code use
    // an "mcpServers" object; VS Code uses "servers".
    public string CopilotCliSnippet => BuildSnippet("mcpServers");
    public string VSCodeSnippet => BuildSnippet("servers");
    public string ClaudeSnippet => BuildSnippet("mcpServers");

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

    [ObservableProperty]
    public partial bool McpInstallOpen { get; set; }

    [ObservableProperty]
    public partial string McpInstallStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity McpInstallSeverity { get; set; } = InfoBarSeverity.Success;

    public void ReportInstall(Tasker.Core.OperationResult result)
    {
        McpInstallStatus = result.Message;
        McpInstallSeverity = result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        McpInstallOpen = true;
    }
}

public sealed partial class AboutPage : Page
{
    public AboutPageViewModel ViewModel { get; } = new();
    private bool _appearanceReady;
    private bool _settingsReady;

    public AboutPage()
    {
        InitializeComponent();

        // Reflect saved preferences without re-triggering an apply during load.
        (AppSettings.Backdrop switch
        {
            BackdropChoice.Mica => BackdropMica,
            BackdropChoice.Acrylic => BackdropAcrylic,
            _ => BackdropMicaAlt,
        }).IsChecked = true;
        (AppSettings.Theme switch
        {
            ThemeChoice.Light => ThemeLight,
            ThemeChoice.Dark => ThemeDark,
            _ => ThemeSystem,
        }).IsChecked = true;
        _appearanceReady = true;

        AiToggle.IsOn = AppSettings.AiEnabled;
        StartAssistant.IsEnabled = AppSettings.AiEnabled;
        OnlyTaskerToggle.IsOn = AppSettings.OnlyTaskerTasks;
        AlwaysAdminToggle.IsOn = AppSettings.AlwaysRunAsAdmin;
        // SelectedIndex maps 1:1 to the StartPage enum order.
        StartPageRadios.SelectedIndex = (int)AppSettings.StartPage;
        _settingsReady = true;

        _ = InitStartupToggleAsync();
    }

    private void StartPage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsReady || StartPageRadios.SelectedIndex < 0) return;
        AppSettings.StartPage = (StartPage)StartPageRadios.SelectedIndex;
    }

    private void AiToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        AppSettings.AiEnabled = AiToggle.IsOn;          // raises AiEnabledChanged -> shell updates
        StartAssistant.IsEnabled = AiToggle.IsOn;
        // If Assistant was the chosen start page but AI is now off, fall back to Tasks.
        if (!AiToggle.IsOn && AppSettings.StartPage == StartPage.Assistant)
            StartPageRadios.SelectedIndex = (int)StartPage.Tasks;
    }

    private void OnlyTaskerToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        // Global filter; each task list reads this when it loads.
        AppSettings.OnlyTaskerTasks = OnlyTaskerToggle.IsOn;
    }

    private async void AlwaysAdminToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        AppSettings.AlwaysRunAsAdmin = AlwaysAdminToggle.IsOn;

        // Enabling it from an unelevated session: offer to apply immediately (it also applies
        // automatically on every future launch). If already elevated there's nothing to do.
        if (AlwaysAdminToggle.IsOn && !Helpers.Elevation.IsElevated)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Restart as administrator?",
                Content = "WinTask Scheduler will start elevated automatically from now on. Restart as administrator now to apply it right away?",
                PrimaryButtonText = "Restart now",
                CloseButtonText = "Later",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && Helpers.Elevation.RelaunchAsAdmin())
                Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }

    // ---- Run on Windows sign-in (MSIX StartupTask) ----
    private const string StartupTaskId = "WinTaskSchedulerStartup";
    private bool _startupReady;

    private async Task InitStartupToggleAsync()
    {
        try
        {
            var task = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
            switch (task.State)
            {
                case Windows.ApplicationModel.StartupTaskState.Enabled:
                case Windows.ApplicationModel.StartupTaskState.EnabledByPolicy:
                    StartupToggle.IsOn = true;
                    break;
                default:
                    StartupToggle.IsOn = false;
                    break;
            }
            if (task.State is Windows.ApplicationModel.StartupTaskState.DisabledByUser)
                ShowStartupNote("Startup is turned off for this app in Task Manager \u2192 Startup apps. Re-enable it there.");
            else if (task.State is Windows.ApplicationModel.StartupTaskState.DisabledByPolicy
                     or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy)
                ShowStartupNote("This setting is managed by your organization.");
        }
        catch
        {
            // StartupTask needs package identity; hide the option if unavailable.
            StartupToggle.IsEnabled = false;
            ShowStartupNote("Run-on-startup isn't available in this build.");
        }
        finally
        {
            _startupReady = true;
        }
    }

    private async void StartupToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!_startupReady) return;
        try
        {
            var task = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
            if (StartupToggle.IsOn)
            {
                var state = await task.RequestEnableAsync();
                if (state is not (Windows.ApplicationModel.StartupTaskState.Enabled
                    or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy))
                {
                    StartupToggle.IsOn = false;
                    ShowStartupNote("Windows blocked enabling startup. Turn it on in Task Manager \u2192 Startup apps.");
                }
                else
                {
                    HideStartupNote();
                }
            }
            else
            {
                task.Disable();
                HideStartupNote();
            }
        }
        catch
        {
            ShowStartupNote("Couldn't change the startup setting.");
        }
    }

    private void ShowStartupNote(string text)
    {
        StartupNote.Text = text;
        StartupNote.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
    }

    private void HideStartupNote() => StartupNote.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

    private void InstallCopilot_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => ViewModel.ReportInstall(McpInstaller.InstallForCopilotCli());

    private void InstallVSCode_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => ViewModel.ReportInstall(McpInstaller.InstallForVSCode());

    private void InstallClaude_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => ViewModel.ReportInstall(McpInstaller.InstallForClaudeCode());

    private void Backdrop_Checked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_appearanceReady && sender is RadioButton { Tag: string tag } && int.TryParse(tag, out var i))
            ThemeManager.SetBackdrop((BackdropChoice)i);
    }

    private void Theme_Checked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_appearanceReady && sender is RadioButton { Tag: string tag } && int.TryParse(tag, out var i))
            ThemeManager.SetTheme((ThemeChoice)i);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            var (server, version) = await TaskerClient.GetConnectionInfoAsync();
            ViewModel.ConnectionText = $"Connected to '{server}' \u2022 Task Scheduler engine v{version}";
        }
        catch (Exception ex)
        {
            ViewModel.ConnectionText = $"Not connected: {ex.Message}";
        }
    }
}

