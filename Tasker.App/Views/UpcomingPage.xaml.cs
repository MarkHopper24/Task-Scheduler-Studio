using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Tasker.Core;
using Tasker_App.Services;
using Tasker_App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Tasker_App.Views;

public sealed partial class UpcomingPage : Page
{
    public UpcomingPageViewModel ViewModel { get; } = new();

    public UpcomingPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();
    }

    private void UpcomingList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is TaskSummaryDto task)
        {
            ViewModel.SelectedTask = task;
            UpcomingContextMenu.ShowAt(UpcomingListView, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = e.GetPosition(UpcomingListView) });
            e.Handled = true;
        }
    }

    private void UpcomingPin_Click(object sender, RoutedEventArgs e)
    {
        var task = ViewModel.SelectedTask;
        if (task is null) return;
        if (AppSettings.IsQuickLaunch(task.Path))
        {
            ViewModel.StatusMessage = $"\u201C{task.Name}\u201D is already in Quick Launch.";
            return;
        }
        AppSettings.AddQuickLaunch(task.Path);
        ViewModel.StatusMessage = $"Pinned \u201C{task.Name}\u201D to Quick Launch.";
    }

    private void UpcomingCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var path = ViewModel.SelectedTask?.Path;
        if (string.IsNullOrEmpty(path)) return;
        ViewModel.StatusMessage = Helpers.Clip.TrySetText(path)
            ? "Copied task path to clipboard."
            : "Couldn't access the clipboard. Try again.";
    }
}
