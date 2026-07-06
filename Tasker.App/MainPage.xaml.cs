using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Linq;
using Tasker_App.Views;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Tasker_App;

/// <summary>
/// The application shell: a NavigationView that hosts the Tasks browser, the Running view,
/// and the Settings page inside its content frame. The footer also exposes a Pin toggle that
/// keeps the window above other windows, and it reacts to Quick Launch's "minimal view" widget mode.
/// </summary>
public sealed partial class MainPage : Page
{
    private bool _pinned;

    private SizeInt32? _restoreSize;

    public MainPage()
    {
        InitializeComponent();

        // Hide the Assistant when AI is disabled, then open the user's chosen start page.
        ApplyAiVisibility(Services.AppSettings.AiEnabled);
        var (page, item) = ResolveStartPage();
        var startWidget = Services.AppSettings.StartPage == Services.StartPage.QuickLaunchWidget
            && page == typeof(QuickLaunchPage);
        ContentFrame.Navigate(page, startWidget ? "widget" : null);
        NavView.SelectedItem = item;

        Services.WidgetMode.Changed += OnWidgetModeChanged;
        Services.AppSettings.AiEnabledChanged += OnAiEnabledChanged;
        // Keep the nav selection in sync when pages navigate programmatically (e.g. the Designer
        // returning to Tasks after a save, or "Open in designer" from the task list).
        ContentFrame.Navigated += OnContentFrameNavigated;
    }

    private void OnContentFrameNavigated(object sender, NavigationEventArgs e)
    {
        var tag = e.SourcePageType.Name switch
        {
            nameof(QuickLaunchPage) => "QuickLaunch",
            nameof(RunningPage) => "Running",
            nameof(UpcomingPage) => "Upcoming",
            nameof(AssistantPage) => "Assistant",
            nameof(AboutPage) => "Settings",
            nameof(DesignerPage) => "Designer",
            nameof(LibraryPage) => "Library",
            _ => "Tasks",
        };
        var match = NavView.MenuItems.Concat(NavView.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(i => (i.Tag as string) == tag);
        if (match is not null && !ReferenceEquals(NavView.SelectedItem, match))
            NavView.SelectedItem = match;
    }

    private (Type page, NavigationViewItem item) ResolveStartPage()
    {
        var aiOn = Services.AppSettings.AiEnabled;
        return Services.AppSettings.StartPage switch
        {
            Services.StartPage.QuickLaunch => (typeof(QuickLaunchPage), QuickLaunchItem),
            Services.StartPage.QuickLaunchWidget => (typeof(QuickLaunchPage), QuickLaunchItem),
            Services.StartPage.Assistant when aiOn => (typeof(AssistantPage), AssistantItem),
            _ => (typeof(TasksPage), TasksItem),
        };
    }

    private void ApplyAiVisibility(bool enabled)
    {
        AssistantItem.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAiEnabledChanged(bool enabled)
    {
        // AppSettings is thread-agnostic static state; marshal to the UI thread before touching
        // any UI element (a cross-thread access throws COMException in WinUI 3).
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => OnAiEnabledChanged(enabled));
            return;
        }
        ApplyAiVisibility(enabled);
        // If AI was turned off while on the Assistant page, fall back to Tasks.
        if (!enabled && ContentFrame.CurrentSourcePageType == typeof(AssistantPage))
        {
            ContentFrame.Navigate(typeof(TasksPage));
            NavView.SelectedItem = TasksItem;
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        var page = (item.Tag as string) switch
        {
            "QuickLaunch" => typeof(QuickLaunchPage),
            "Designer" => typeof(DesignerPage),
            "Library" => typeof(LibraryPage),
            "Running" => typeof(RunningPage),
            "Upcoming" => typeof(UpcomingPage),
            "Assistant" => typeof(AssistantPage),
            "Settings" => typeof(AboutPage),
            _ => typeof(TasksPage),
        };
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page);
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: "Pin" })
            TogglePin();
    }

    /// <summary>Toggles whether the window stays above other windows, updating the footer button.</summary>
    private void TogglePin() => ApplyPinned(!_pinned);

    /// <summary>Sets the window's always-on-top state and syncs the footer Pin button. This is the
    /// single source of truth for the pin preference, shared by the nav footer and the widget.</summary>
    public void ApplyPinned(bool pinned)
    {
        if (App.Window?.AppWindow?.Presenter is not OverlappedPresenter presenter) return;

        _pinned = pinned;
        presenter.IsAlwaysOnTop = pinned;

        PinItem.Content = pinned ? "Unpin" : "Pin";
        ToolTipService.SetToolTip(PinItem, pinned ? "Stop keeping this window on top" : "Keep this window above other windows");
        if (PinItem.Icon is FontIcon icon)
            icon.Glyph = pinned ? "\uE77A" : "\uE718"; // UnPin : Pin
    }

    /// <summary>True when the window is currently kept above other windows.</summary>
    public bool IsWindowPinned => _pinned;

    /// <summary>Enters/exits Quick Launch's floating "widget" mode: hides the navigation pane and
    /// the window title bar, shrinks the window, switches to a translucent backdrop, and floats it
    /// on top so just the pinned task buttons remain.</summary>
    private void OnWidgetModeChanged(bool active)
    {
        // WidgetMode is thread-agnostic static state; marshal to the UI thread before touching UI.
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => OnWidgetModeChanged(active));
            return;
        }
        NavView.IsPaneVisible = !active;

        var window = App.Window;
        var appWindow = window?.AppWindow;
        if (window is null || appWindow is null) return;

        var scale = (window.Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1.0;

        if (appWindow.Presenter is OverlappedPresenter p)
        {
            if (active)
            {
                _restoreSize = appWindow.Size;
                // The widget floats on top regardless of the saved pin preference.
                p.IsAlwaysOnTop = true;
                // Translucent backdrop so the pinned buttons appear to float (not persisted). The
                // page extends content into the title bar and makes the header the draggable title
                // bar, so the OS title text is replaced.
                Services.ThemeManager.ApplyBackdrop(window, Services.BackdropChoice.Acrylic);
                appWindow.Resize(new SizeInt32((int)(320 * scale), (int)(460 * scale)));
            }
            else
            {
                // Restore the user's pin preference (which the widget's pin button may have changed).
                p.IsAlwaysOnTop = _pinned;
                // Restore the user's saved backdrop choice.
                Services.ThemeManager.ApplyBackdrop(window, Services.AppSettings.Backdrop);
                if (_restoreSize is { } size) appWindow.Resize(size);
            }
        }
    }
}
