using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tasker.Core;
using Tasker_App.ViewModels;
using Windows.UI;

namespace Tasker_App.Helpers;

/// <summary>Static x:Bind helper functions (used instead of IValueConverters).</summary>
public static class Format
{
    public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility InvertBoolToVisibility(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static bool Not(bool value) => !value;

    public static HorizontalAlignment ChatAlignment(bool isUser) =>
        isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    // Chat bubble colours are computed explicitly per theme rather than read from system
    // resources: Application.Current.Resources lookups don't honour the element's actual theme,
    // which made assistant replies render near-white (invisible) under the Light theme.
    public static Brush ChatBubbleBrush(ChatRole role)
    {
        var dark = IsDarkTheme();
        var c = role switch
        {
            ChatRole.User => Hex(0x0F6CBD),                       // accent blue (both themes)
            ChatRole.Error => dark ? Hex(0x5A1B1F) : Hex(0xFDE7E9),
            ChatRole.Tool => dark ? Hex(0x143C25) : Hex(0xDFF6E5),
            _ => dark ? Hex(0x2D2D2D) : Hex(0xF2F2F2),            // assistant card
        };
        return new SolidColorBrush(c);
    }

    public static Brush ChatTextBrush(ChatRole role)
    {
        if (role == ChatRole.User) return new SolidColorBrush(Colors.White);
        var dark = IsDarkTheme();
        return new SolidColorBrush(dark ? Hex(0xF3F3F3) : Hex(0x1A1A1A));
    }

    private static bool IsDarkTheme()
    {
        if ((App.Window?.Content as FrameworkElement)?.ActualTheme is { } t)
            return t == ElementTheme.Dark;
        return Application.Current.RequestedTheme == ApplicationTheme.Dark;
    }

    private static Color Hex(uint rgb) =>
        Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static InfoBarSeverity Severity(bool hasError) =>
        hasError ? InfoBarSeverity.Error : InfoBarSeverity.Success;

    public static string DateText(DateTime? value) =>
        value is { } d ? d.ToString("g") : "-";

    /// <summary>Compact "last run" line for the Quick Launch cards/widget: when it last ran and the
    /// result. Treats the Task Scheduler "never run" sentinel (year &lt; 2000) as "never".</summary>
    public static string LastRunSummary(DateTime? lastRun, string resultText)
    {
        if (lastRun is not { } t || t.Year < 2000) return "Last run: never";
        var when = t.ToString("g");
        return string.IsNullOrWhiteSpace(resultText) ? $"Last run: {when}" : $"Last run: {when} \u2022 {resultText}";
    }

    public static string StateGlyph(TaskRunState state) => state switch
    {
        TaskRunState.Running => "\uE768",   // Play
        TaskRunState.Ready => "\uE73E",     // CheckMark
        TaskRunState.Disabled => "\uE711",  // Cancel
        TaskRunState.Queued => "\uE823",    // Clock
        _ => "\uE9CE",                       // Unknown / Help
    };

    public static Brush StateBrush(TaskRunState state)
    {
        var color = state switch
        {
            TaskRunState.Running => Color.FromArgb(255, 0x6C, 0xCB, 0x5F),
            TaskRunState.Ready => Color.FromArgb(255, 0x60, 0xCD, 0xFF),
            TaskRunState.Disabled => Color.FromArgb(255, 0x9A, 0x9A, 0x9A),
            TaskRunState.Queued => Color.FromArgb(255, 0xF7, 0x99, 0x2E),
            _ => Color.FromArgb(255, 0x9A, 0x9A, 0x9A),
        };
        return new SolidColorBrush(color);
    }

    public static string EnabledText(bool enabled) => enabled ? "Enabled" : "Disabled";

    public static string NotEmptyOrDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    /// <summary>Friendly folder label: blank for the root folder ("\") so cards/lists don't show a lone backslash.</summary>
    public static string FolderLabel(string? folder) =>
        string.IsNullOrEmpty(folder) || folder == "\\" ? string.Empty : folder;

    /// <summary>Collapses a folder line when the task lives in the root folder (nothing useful to show).</summary>
    public static Visibility FolderVisibility(string? folder) =>
        string.IsNullOrEmpty(folder) || folder == "\\" ? Visibility.Collapsed : Visibility.Visible;

    public static bool IsNotEmpty(string value) => !string.IsNullOrWhiteSpace(value);

    /// <summary>Visible only when a collection is empty (for empty-state prompts).</summary>
    public static Visibility VisibleWhenEmpty(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Visible only when a collection has at least one item.</summary>
    public static Visibility VisibleWhenAny(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static string AuthGlyph(GhAuthState state) => state switch
    {
        GhAuthState.SignedIn => "\uE73E",   // CheckMark
        GhAuthState.SignedOut => "\uE7BA",  // Warning
        GhAuthState.Error => "\uE783",      // ErrorBadge
        _ => "\uE9F5",                       // Sync (checking)
    };

    public static Brush AuthBrush(GhAuthState state)
    {
        var color = state switch
        {
            GhAuthState.SignedIn => Color.FromArgb(255, 0x4C, 0xC2, 0x5E),  // green
            GhAuthState.SignedOut => Color.FromArgb(255, 0xE8, 0x9A, 0x3C), // amber
            GhAuthState.Error => Color.FromArgb(255, 0xE0, 0x4F, 0x4F),     // red
            _ => Color.FromArgb(255, 0x9A, 0x9A, 0x9A),                      // gray
        };
        return new SolidColorBrush(color);
    }

    /// <summary>Sign-in button is only useful while signed out (or still checking); once signed in
    /// (or a verification error, which still means an account is locally recorded) the Sign out /
    /// Switch account controls take its place.</summary>
    public static Visibility VisibleWhenSignedOut(GhAuthState state) =>
        state is GhAuthState.SignedOut or GhAuthState.Checking ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleWhenSignedIn(GhAuthState state) =>
        state is GhAuthState.SignedIn or GhAuthState.Error ? Visibility.Visible : Visibility.Collapsed;
}
