using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Tasker.Core;
using Tasker_App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Tasker_App.Views;

public sealed partial class RunningPage : Page
{
    public RunningPageViewModel ViewModel { get; } = new();

    public RunningPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();
    }

    private void RunningList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is RunningTaskDto task)
        {
            ViewModel.SelectedTask = task;
            RunningContextMenu.ShowAt(RunningListView, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = e.GetPosition(RunningListView) });
            e.Handled = true;
        }
    }

    private void RunningCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var path = ViewModel.SelectedTask?.Path;
        if (string.IsNullOrEmpty(path)) return;
        var data = new DataPackage();
        data.SetText(path);
        Clipboard.SetContent(data);
        ViewModel.StatusMessage = "Copied task path to clipboard.";
    }
}
