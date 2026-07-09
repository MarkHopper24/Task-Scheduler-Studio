using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Runtime.InteropServices;
using Tasker.Core;
using Tasker_App.Dialogs;
using Tasker_App.Services;
using Tasker_App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Tasker_App.Views;

/// <summary>Gallery of pinned tasks that can be launched with one click.</summary>
public sealed partial class QuickLaunchPage : Page
{
    public QuickLaunchPageViewModel ViewModel { get; } = new();

    public QuickLaunchPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.IsWidget))
                ApplyWidgetTitleBar(ViewModel.IsWidget);
        };
        // Manual window dragging from any empty surface in the widget (the body's ScrollViewer
        // isn't a system drag region and marks the press handled, so listen for handled events too).
        // Buttons/rows are skipped in the handler so their clicks still work.
        WidgetView.AddHandler(PointerPressedEvent, new PointerEventHandler(WidgetView_PointerPressed), handledEventsToo: true);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();

        // Launched directly into the floating widget (start-page preference).
        if (e.Parameter as string == "widget" && !ViewModel.IsWidget)
            DispatcherQueue.TryEnqueue(() => ViewModel.IsWidget = true);
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskSummaryDto task)
            await ViewModel.RunCommand.ExecuteAsync(task.Path);
    }

    private void Unpin_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskSummaryDto task)
            ViewModel.UnpinCommand.Execute(task.Path);
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TaskSummaryDto task) return;
        var ok = Helpers.Clip.TrySetText(task.Path);
        ViewModel.ShowStatus(ok ? "Copied task path to clipboard." : "Couldn't access the clipboard. Try again.", isError: !ok);
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var page = new QuickLaunchPickerDialogPage(AppSettings.QuickLaunch);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add to Quick Launch",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = page,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720d;
        dialog.Resources["ContentDialogMaxHeight"] = 760d;
        dialog.PrimaryButtonClick += page.OnSave;

        Services.ThemeManager.ApplyToDialog(dialog);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ApplyPinnedAsync(page.SelectedPaths);
            ViewModel.ShowStatus($"Quick Launch updated ({page.SelectedPaths.Count} pinned).", isError: false);
        }
    }

    private void ToggleWidget_Click(object sender, RoutedEventArgs e)
        => ViewModel.IsWidget = !ViewModel.IsWidget;

    private void WidgetClose_Click(object sender, RoutedEventArgs e)
        => App.Window?.Close();

    /// <summary>Pins/unpins the floating widget (window always-on-top), routed through the shell
    /// page so the nav footer's Pin button stays in sync and the choice survives leaving the widget.</summary>
    private void WidgetPin_Click(object sender, RoutedEventArgs e)
    {
        if (App.Window?.AppWindow?.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter p) return;
        var pinned = !p.IsAlwaysOnTop;
        (App.Window as MainWindow)?.HostedPage?.ApplyPinned(pinned);
        UpdateWidgetPin(pinned);
    }

    private void UpdateWidgetPin(bool pinned)
    {
        QuickLaunchWidgetPinIcon.Glyph = pinned ? "\uE77A" : "\uE718"; // UnPin : Pin
        ToolTipService.SetToolTip(QuickLaunchWidgetPinButton,
            pinned ? "Stop keeping on top" : "Keep on top");
    }

    /// <summary>In widget mode, hide the app title bar/caption buttons. The window has no system
    /// drag region, so <see cref="WidgetView_PointerPressed"/> moves the window from any empty
    /// surface. Restored to normal on exit.</summary>
    private void ApplyWidgetTitleBar(bool widget)
    {
        if (App.Window is not MainWindow mw) return;
        if (widget)
            DispatcherQueue.TryEnqueue(() =>
            {
                mw.EnterWidgetChrome();
                // Widget mode floats on top; reflect the actual presenter state in the pin button.
                var pinned = mw.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { IsAlwaysOnTop: true };
                UpdateWidgetPin(pinned);
            });
        else
            mw.ExitWidgetChrome();
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 0x0002;

    /// <summary>Lets the user drag the floating widget by pressing any empty (non-interactive) area.
    /// Interactive controls (buttons, the pinned-task rows) are skipped so their clicks still work.</summary>
    private void WidgetView_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!ViewModel.IsWidget) return;
        if (!e.GetCurrentPoint(WidgetView).Properties.IsLeftButtonPressed) return;
        if (IsInteractive(e.OriginalSource as DependencyObject)) return;

        // Hand off to the OS window-move loop so the window follows the cursor until release.
        ReleaseCapture();
        SendMessage(App.WindowHandle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        e.Handled = true;
    }

    /// <summary>True if <paramref name="source"/> is, or sits inside, an interactive control whose
    /// own pointer handling should win over window dragging.</summary>
    private bool IsInteractive(DependencyObject? source)
    {
        for (var node = source; node is not null && node != WidgetView; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase or SelectorItem or ScrollBar or TextBox)
                return true;
        }
        return false;
    }
}
