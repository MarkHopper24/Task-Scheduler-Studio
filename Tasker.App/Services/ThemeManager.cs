using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Tasker_App.Services;

/// <summary>Applies and persists the window backdrop (Mica Alt / Mica / Acrylic) and app theme
/// (System / Light / Dark), and keeps the caption buttons legible across themes.</summary>
public static class ThemeManager
{
    /// <summary>Applies the saved preferences to a window (called at startup).</summary>
    public static void ApplyTo(Window window)
    {
        ApplyTheme(window, AppSettings.Theme);
        ApplyBackdrop(window, AppSettings.Backdrop);
    }

    public static void SetBackdrop(BackdropChoice choice)
    {
        AppSettings.Backdrop = choice;
        if (App.Window is { } window) ApplyBackdrop(window, choice);
    }

    public static void SetTheme(ThemeChoice choice)
    {
        AppSettings.Theme = choice;
        if (App.Window is { } window) ApplyTheme(window, choice);
    }

    public static void ApplyBackdrop(Window window, BackdropChoice choice)
    {
        window.SystemBackdrop = choice switch
        {
            BackdropChoice.Mica => new MicaBackdrop { Kind = MicaKind.Base },
            BackdropChoice.Acrylic => new DesktopAcrylicBackdrop(),
            _ => new MicaBackdrop { Kind = MicaKind.BaseAlt },
        };
    }

    public static void ApplyTheme(Window window, ThemeChoice choice)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = choice switch
            {
                ThemeChoice.Light => ElementTheme.Light,
                ThemeChoice.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
        UpdateCaptionButtons(window);
    }

    private static void UpdateCaptionButtons(Window window)
    {
        try
        {
            var bar = window.AppWindow.TitleBar;
            var dark = (window.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
            bar.ButtonBackgroundColor = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
            bar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
            bar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
            bar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
            bar.ButtonInactiveForegroundColor = dark ? Color.FromArgb(255, 0x9A, 0x9A, 0x9A) : Color.FromArgb(255, 0x60, 0x60, 0x60);
        }
        catch { /* title bar customization unavailable */ }
    }
}
