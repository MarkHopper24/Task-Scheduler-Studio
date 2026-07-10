namespace Tasker_App.ViewModels;

public enum ChatRole { User, Assistant, Tool, Error }

/// <summary>GitHub sign-in state shown by the Assistant page's auth indicator. <c>Error</c> is
/// distinct from <c>SignedOut</c>: it means gh reports a signed-in account locally but that
/// account couldn't be verified against GitHub right now (e.g. no network, or a revoked token) —
/// worth telling the user about specifically instead of implying they need to sign in again.</summary>
public enum GhAuthState { Checking, SignedIn, SignedOut, Error }

/// <summary>A clickable suggested prompt shown on the Assistant page.</summary>
/// <param name="Glyph">A Segoe Fluent Icons glyph rendered by a FontIcon (not embedded in text).</param>
public sealed record AssistantSuggestion(string Glyph, string Label, string Prompt);

/// <summary>A single message in the assistant transcript.</summary>
public sealed class ChatMessage
{
    public ChatRole Role { get; init; }
    public string Text { get; init; } = string.Empty;

    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant;
    public bool IsTool => Role == ChatRole.Tool;
    public bool IsError => Role == ChatRole.Error;

    public string Author => Role switch
    {
        ChatRole.User => "You",
        ChatRole.Assistant => "Copilot",
        ChatRole.Tool => "Task Scheduler Studio",
        ChatRole.Error => "Error",
        _ => string.Empty,
    };

    public static ChatMessage From(ChatRole role, string text) => new() { Role = role, Text = text };
}
