using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Tasker_App.Services;

namespace Tasker_App.ViewModels;

/// <summary>
/// Drives the natural-language Assistant page. Owns the transcript and the GitHub Copilot
/// connection, marshalling all assistant callbacks back to the UI thread.
/// </summary>
public partial class AssistantPageViewModel : ObservableObject
{
    private readonly CopilotAssistant _assistant = new();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public IReadOnlyList<AssistantSuggestion> Suggestions { get; } = new[]
    {
        new AssistantSuggestion("\uE721", "Scan tasks for red flags",
            "Analyze the scheduled tasks in the root (\\) folder for anything suspicious and tell me what you found, with the evidence for each."),
        new AssistantSuggestion("\uE7B5", "What runs at startup?",
            "What scheduled tasks run at system startup or at log on?"),
        new AssistantSuggestion("\uE823", "List everything due today",
            "Which scheduled tasks are set to run in the next 24 hours? List them with their next run time."),
        new AssistantSuggestion("\uE9D9", "Explain a task",
            "Pick one non-Microsoft scheduled task and explain in plain language what it does, when it runs, and whether it looks safe."),
        new AssistantSuggestion("\uE710", "Run Notepad daily",
            "Create a task that runs Notepad every day at 9 AM."),
        new AssistantSuggestion("\uE896", "Nightly Downloads cleanup",
            "Create a task that moves files older than 30 days from my Downloads folder to the Recycle Bin every night at 2 AM."),
        new AssistantSuggestion("\uEB9F", "Weekly script",
            "Help me schedule a PowerShell script to run every Monday morning. Ask me for the script path and time."),
    };

    [ObservableProperty]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string TokenInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConnectionStatus { get; set; } = "Sign in with GitHub, or just send a message to use your existing Copilot session.";

    // ---- GitHub auth status indicator ----
    [ObservableProperty]
    public partial GhAuthState AuthState { get; set; } = GhAuthState.Checking;

    [ObservableProperty]
    public partial string AuthStatusText { get; set; } = "Checking sign-in\u2026";

    /// <summary>Probes GitHub sign-in: prefers the gh CLI's logged-in user, falling back to a saved
    /// token, otherwise reports signed-out. Safe to call repeatedly (e.g. on navigation / after auth).</summary>
    public async Task RefreshAuthStatusAsync()
    {
        AuthState = GhAuthState.Checking;
        AuthStatusText = "Checking sign-in\u2026";

        string? user = null;
        try
        {
            if (await GitHubAuth.IsAvailableAsync())
                user = await GitHubAuth.GetUserLoginAsync();
        }
        catch { /* treated as signed-out below */ }

        if (user is not null)
        {
            AuthState = GhAuthState.SignedIn;
            AuthStatusText = $"Signed in as {user}";
        }
        else if (_assistant.HasStoredToken)
        {
            AuthState = GhAuthState.SignedIn;
            AuthStatusText = "Signed in with saved token";
        }
        else
        {
            AuthState = GhAuthState.SignedOut;
            AuthStatusText = "Not signed in";
        }
    }

    // ---- Model + reasoning-effort selection ----
    private static readonly ModelOption AutoOption = new("auto", "Automatic (recommended)", Array.Empty<string>(), null);

    public ObservableCollection<ModelOption> Models { get; } = new();
    public ObservableCollection<string> ReasoningEfforts { get; } = new();

    [ObservableProperty]
    public partial ModelOption? SelectedModelOption { get; set; }

    [ObservableProperty]
    public partial string? SelectedReasoningEffort { get; set; }

    [ObservableProperty]
    public partial bool ModelsLoading { get; set; }

    public bool CanPickEffort => ReasoningEfforts.Count > 1;

    private bool _suppressApply;

    [RelayCommand]
    private async Task LoadModelsAsync()
    {
        if (ModelsLoading) return;
        ModelsLoading = true;
        try
        {
            var list = await _assistant.ListModelsAsync();
            var currentId = SelectedModelOption?.Id ?? "auto";
            Models.Clear();
            Models.Add(AutoOption);
            // Skip the SDK's own "auto" entry — AutoOption already represents it (as "Automatic (recommended)").
            foreach (var m in list)
            {
                if (string.Equals(m.Id, AutoOption.Id, StringComparison.OrdinalIgnoreCase)) continue;
                Models.Add(m);
            }
            SelectedModelOption = Models.FirstOrDefault(m => m.Id == currentId) ?? AutoOption;
            if (list.Count > 0)
                ConnectionStatus = $"Loaded {list.Count} model(s). Pick one, or leave it on Automatic.";
        }
        finally
        {
            ModelsLoading = false;
        }
    }

    async partial void OnSelectedModelOptionChanged(ModelOption? value)
    {
        _suppressApply = true;
        ReasoningEfforts.Clear();
        ReasoningEfforts.Add("Default");
        if (value is not null)
            foreach (var e in value.Efforts) ReasoningEfforts.Add(e);
        OnPropertyChanged(nameof(CanPickEffort));
        SelectedReasoningEffort = "Default";
        _suppressApply = false;
        await ApplyModelSelectionAsync();
    }

    async partial void OnSelectedReasoningEffortChanged(string? value)
    {
        if (_suppressApply) return;
        await ApplyModelSelectionAsync();
    }

    private async Task ApplyModelSelectionAsync()
    {
        _assistant.SelectedModel = SelectedModelOption?.Id ?? "auto";
        _assistant.SelectedReasoningEffort =
            (SelectedReasoningEffort is null or "Default") ? null : SelectedReasoningEffort;
        // The model/effort is fixed at session creation, so drop the current session;
        // the next message reconnects with the new selection.
        await _assistant.ResetAsync();
    }

    public bool HasStoredToken => _assistant.HasStoredToken;


    public AssistantPageViewModel()
    {
        _assistant.AssistantMessage += text => Post(() => AddMessage(ChatRole.Assistant, text));
        _assistant.ToolActivity += text => Post(() => SetLastToolActivity(text));
        _assistant.ErrorMessage += text => Post(() => AddMessage(ChatRole.Error, text));

        // Seed with the Automatic option; the full list loads on demand (first dropdown open).
        Models.Add(AutoOption);
        SelectedModelOption = AutoOption;

        if (_assistant.HasStoredToken)
            ConnectionStatus = "Using a saved GitHub token.";

        Messages.Add(ChatMessage.From(ChatRole.Assistant,
            "Hi! Tell me what you'd like to schedule. For example, " +
            "\u201Crun Notepad every weekday at 9am\u201D or \u201Cback up my docs folder nightly.\u201D " +
            "I'll ask for anything I'm missing, then create the task for you."));
    }

    private bool CanSend => !IsBusy && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var prompt = InputText.Trim();
        if (string.IsNullOrEmpty(prompt)) return;

        InputText = string.Empty;
        AddMessage(ChatRole.User, prompt);
        IsBusy = true;
        try
        {
            if (!await _assistant.EnsureStartedAsync())
                return; // error already surfaced via ErrorMessage

            await _assistant.SendAsync(prompt);
        }
        catch (Exception ex)
        {
            AddMessage(ChatRole.Error, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignInWithGitHubAsync()
    {
        ConnectionStatus = "Signing in with GitHub\u2026";
        try
        {
            if (!await GitHubAuth.IsAvailableAsync())
            {
                ConnectionStatus = "GitHub CLI (gh) isn't installed or on PATH. Install it, or paste a token below.";
                return;
            }

            var token = await GitHubAuth.TryGetTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                ConnectionStatus = "Opening GitHub sign-in\u2026 complete it in the window that appears.";
                var ok = await GitHubAuth.LoginInteractiveAsync();
                if (!ok)
                {
                    ConnectionStatus = "GitHub sign-in was cancelled.";
                    return;
                }
                token = await GitHubAuth.TryGetTokenAsync();
            }

            if (string.IsNullOrEmpty(token))
            {
                ConnectionStatus = "Couldn't get a token from the GitHub CLI.";
                return;
            }

            _assistant.StoreToken(token);
            await _assistant.ResetAsync();
            var user = await GitHubAuth.GetUserLoginAsync();
            ConnectionStatus = user is null ? "Signed in with GitHub." : $"Signed in as {user}.";
            OnPropertyChanged(nameof(HasStoredToken));
            await RefreshAuthStatusAsync();
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Sign-in failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveTokenAsync()
    {
        var token = TokenInput.Trim();
        if (string.IsNullOrEmpty(token)) return;

        _assistant.StoreToken(token);
        TokenInput = string.Empty;
        await _assistant.ResetAsync();
        ConnectionStatus = "Saved GitHub token. It will be used on your next message.";
        OnPropertyChanged(nameof(HasStoredToken));
        await RefreshAuthStatusAsync();
    }

    [RelayCommand]
    private async Task ClearTokenAsync()
    {
        _assistant.ClearToken();
        await _assistant.ResetAsync();
        ConnectionStatus = "Removed saved token. Using your signed-in GitHub Copilot account.";
        OnPropertyChanged(nameof(HasStoredToken));
        await RefreshAuthStatusAsync();
    }

    partial void OnInputTextChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    private void AddMessage(ChatRole role, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Messages.Add(ChatMessage.From(role, text));
    }

    private void SetLastToolActivity(string text)
    {
        // Show tool activity as a transient assistant-side note (deduped).
        if (Messages.Count > 0 && Messages[^1].Role == ChatRole.Tool && Messages[^1].Text == text) return;
        Messages.Add(ChatMessage.From(ChatRole.Tool, text));
    }

    private void Post(Action action) => _dispatcher.TryEnqueue(() => action());
}
