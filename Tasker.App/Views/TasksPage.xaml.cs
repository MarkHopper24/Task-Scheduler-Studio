using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tasker.Core;
using Tasker_App.Dialogs;
using Tasker_App.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Tasker_App.Views;

public sealed partial class TasksPage : Page
{
    public TasksPageViewModel ViewModel { get; } = new();

    public TasksPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        FolderColumn.Width = ViewModel.IsFolderPaneOpen ? new GridLength(280) : new GridLength(0);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.HasDetail))
        {
            // Give the details pane a generous share of the height (and let the splitter tune it),
            // collapsing it entirely when nothing is selected so the table uses the full area.
            DetailRow.Height = ViewModel.HasDetail
                ? new GridLength(1.4, GridUnitType.Star)
                : new GridLength(0);
        }
        else if (e.PropertyName == nameof(ViewModel.IsFolderPaneOpen))
        {
            FolderColumn.Width = ViewModel.IsFolderPaneOpen ? new GridLength(280) : new GridLength(0);
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ViewModel.Folders.Count == 0)
            await ViewModel.InitializeAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // A new page + VM is created on every navigation to Tasks; release the static event
        // subscription (and our own) so the old instances don't leak or fire redundant refreshes.
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Cleanup();
    }

    private void FolderTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is FolderNode node)
            ViewModel.SelectedFolder = node;
    }

    private void FolderTree_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        // Focus the right-clicked folder so the context menu's actions target it, then show the menu.
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FolderNode node)
        {
            ViewModel.SelectedFolder = node;
            FolderContextMenu.ShowAt(FolderTree, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = e.GetPosition(FolderTree) });
            e.Handled = true;
        }
    }

    private void TaskList_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        // Select the right-clicked task so the context menu's actions target it, then show the menu.
        if ((e.OriginalSource as FrameworkElement)?.DataContext is Tasker.Core.TaskSummaryDto task)
        {
            if (ViewModel.SelectedTask != task)
                ViewModel.SelectedTask = task;
            TaskContextMenu.ShowAt(TaskListView, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = e.GetPosition(TaskListView) });
            e.Handled = true;
        }
    }

    private void PinQuickLaunch_Click(object sender, RoutedEventArgs e)
    {
        var task = ViewModel.SelectedTask;
        if (task is null) return;
        if (!task.Enabled)
        {
            ViewModel.ShowInfo("Only enabled tasks can be added to Quick Launch. Enable it first.");
            return;
        }
        if (Services.AppSettings.IsQuickLaunch(task.Path))
        {
            ViewModel.ShowInfo($"\u201C{task.Name}\u201D is already in Quick Launch.");
            return;
        }
        Services.AppSettings.AddQuickLaunch(task.Path);
        ViewModel.ShowInfo($"Pinned \u201C{task.Name}\u201D to Quick Launch.");
    }

    private void TaskList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView list)
            ViewModel.SetSelection(list.SelectedItems.OfType<Tasker.Core.TaskSummaryDto>());
    }

    private async void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var paths = ViewModel.SelectedPaths.ToList();
        if (paths.Count == 0) return;

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete tasks",
            Content = $"Permanently delete {paths.Count} selected task(s)? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        Services.ThemeManager.ApplyToDialog(confirm);
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        await ViewModel.BulkAsync(p => Services.TaskerClient.DeleteTaskAsync(p), "Deleted");
    }

    private async void NewTask_Click(object sender, RoutedEventArgs e)
    {
        var initialFolder = ViewModel.SelectedFolder?.Path ?? "\\";
        await ShowEditorAsync(new TaskEditorViewModel { }, initialFolder);
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Detail is null) return;
        await ShowEditorAsync(new TaskEditorViewModel(ViewModel.Detail), ViewModel.Detail.Folder);
    }

    private async void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Detail is null) return;
        await ShowEditorAsync(new TaskEditorViewModel(ViewModel.Detail, asDuplicate: true), ViewModel.Detail.Folder);
    }

    private async void Move_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Detail is null) return;
        var path = ViewModel.Detail.Path;

        var page = new Dialogs.MoveToFolderDialogPage(1, ViewModel.Detail.Folder);
        if (!await ShowMoveDialogAsync(page)) return;

        var result = await ViewModel.MoveTaskAsync(path, page.DestinationFolder);
        if (result.Success)
        {
            ViewModel.ShowInfo(result.Message);
            ViewModel.SelectedTask = null;
            await ViewModel.RefreshAsync();
        }
        else
        {
            ViewModel.ShowError(result.Message);
        }
    }

    private async void BulkMove_Click(object sender, RoutedEventArgs e)
    {
        var paths = ViewModel.SelectedPaths.ToList();
        if (paths.Count == 0) return;

        var page = new Dialogs.MoveToFolderDialogPage(paths.Count, ViewModel.SelectedFolder?.Path ?? "\\");
        if (!await ShowMoveDialogAsync(page)) return;

        await ViewModel.BulkAsync(p => ViewModel.MoveTaskAsync(p, page.DestinationFolder), "Moved", paths);
    }

    /// <summary>Shows the destination-folder picker for a move and reports whether the user
    /// confirmed it (<see cref="Dialogs.MoveToFolderDialogPage.Moved"/>). The caller performs the
    /// actual move(s) afterwards, since single vs. bulk moves report progress differently.</summary>
    private async Task<bool> ShowMoveDialogAsync(Dialogs.MoveToFolderDialogPage page)
    {
        try { page.SetFolders(await Services.TaskerClient.GetFolderPathsAsync()); }
        catch { /* folder suggestions are best-effort */ }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Move to folder",
            PrimaryButtonText = "Move",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = page,
        };
        dialog.PrimaryButtonClick += page.OnMove;

        Services.ThemeManager.ApplyToDialog(dialog);
        await dialog.ShowAsync();
        return page.Moved;
    }

    private async void Template_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string tagStr || !int.TryParse(tagStr, out var idx)) return;
        if (idx < 0 || idx >= TaskTemplates.All.Count) return;
        var vm = TaskTemplates.All[idx].Create();
        await ShowEditorAsync(vm, ViewModel.SelectedFolder?.Path);
    }

    private void DesignVisually_Click(object sender, RoutedEventArgs e)
    {
        var folder = ViewModel.SelectedFolder?.Path ?? "\\";
        Frame.Navigate(typeof(DesignerPage), new DesignerNavArgs { DefaultFolder = folder });
    }

    private void EditInDesigner_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Detail is null) return;
        Frame.Navigate(typeof(DesignerPage), new DesignerNavArgs { Existing = ViewModel.Detail });
    }

    private async void GuidedSetup_Click(object sender, RoutedEventArgs e)
    {
        var page = new Dialogs.WizardDialogPage(ViewModel.SelectedFolder?.Path);
        try { page.SetFolders(await Services.TaskerClient.GetFolderPathsAsync()); }
        catch { /* folder suggestions are best-effort */ }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Guided setup",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = page,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 900d;
        dialog.Resources["ContentDialogMaxHeight"] = 760d;
        dialog.PrimaryButtonClick += page.OnPrimary;
        dialog.SecondaryButtonClick += page.OnSecondary;
        page.Attach(dialog);

        Services.ThemeManager.ApplyToDialog(dialog);
        await dialog.ShowAsync();
        if (page.Created)
        {
            ViewModel.ShowInfo(page.LastResult?.Message ?? "Task created.");
            await ViewModel.RefreshAsync();
        }
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var folder = ViewModel.SelectedFolder?.Path ?? "\\";
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("Task backup (zip)", new List<string> { ".zip" });
        picker.SuggestedFileName = "tasks-backup";

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            using var ms = new MemoryStream();
            var count = await Services.BackupService.BackupFolderAsync(folder, ms);
            ms.Position = 0;
            await using (var fs = await file.OpenStreamForWriteAsync())
            {
                fs.SetLength(0);
                await ms.CopyToAsync(fs);
            }
            ViewModel.ShowInfo($"Backed up {count} task(s) from {folder} to {file.Name}.");
        }
        catch (Exception ex)
        {
            ViewModel.ShowError($"Backup failed: {ex.Message}");
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.FileTypeFilter.Add(".zip");
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var folder = ViewModel.SelectedFolder?.Path ?? "\\";
        try
        {
            await using var stream = await file.OpenStreamForReadAsync();
            var (restored, failed) = await Services.BackupService.RestoreAsync(stream, folder);
            ViewModel.ShowInfo($"Restored {restored} task(s)" + (failed > 0 ? $", {failed} failed." : "."));
            await ViewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            ViewModel.ShowError($"Restore failed: {ex.Message}");
        }
    }

    private void RestartAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (Helpers.Elevation.RelaunchAsAdmin())
            Application.Current.Exit();
        else
            ViewModel.ShowError("Couldn't restart as administrator (the prompt may have been cancelled).");
    }

    private async Task ShowEditorAsync(TaskEditorViewModel editorVm, string? defaultFolder)
    {
        if (!editorVm.IsEditMode && !string.IsNullOrEmpty(defaultFolder))
            editorVm.Folder = defaultFolder!;

        try { editorVm.SetFolders(await Services.TaskerClient.GetFolderPathsAsync()); }
        catch { /* folder suggestions are best-effort */ }

        var page = new TaskEditorDialogPage(editorVm);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = editorVm.Title,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonText = "Save",
            Content = page,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 860d;
        dialog.Resources["ContentDialogMaxHeight"] = 1600d;
        dialog.PrimaryButtonClick += page.OnSave;

        Services.ThemeManager.ApplyToDialog(dialog);
        await dialog.ShowAsync();
        if (page.Saved)
        {
            ViewModel.ShowInfo(page.LastResult?.Message ?? "Saved.");
            await ViewModel.RefreshAsync();
        }
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var parent = ViewModel.SelectedFolder?.Path ?? "\\";
        var input = new TextBox { PlaceholderText = "Folder name", AcceptsReturn = false };
        AutomationProperties.SetAutomationId(input, "NewFolderNameBox");
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"New folder in {parent}",
            Content = input,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        Services.ThemeManager.ApplyToDialog(dialog);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var name = input.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var path = parent.EndsWith('\\') ? parent + name : parent + "\\" + name;

        var result = await Services.TaskerClient.CreateFolderAsync(path);
        if (result.Success) { ViewModel.ShowInfo(result.Message); await ViewModel.LoadFoldersAsync(); }
        else ViewModel.ShowError(result.Message);
    }

    private async void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = ViewModel.SelectedFolder?.Path;
        if (string.IsNullOrEmpty(folder) || folder == "\\")
        {
            ViewModel.ShowError("Select a non-root folder to delete.");
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete folder",
            Content = $"Delete the folder '{folder}'? It must be empty (no tasks or subfolders).",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        Services.ThemeManager.ApplyToDialog(confirm);
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        var result = await Services.TaskerClient.DeleteFolderAsync(folder);
        if (result.Success)
        {
            ViewModel.ShowInfo(result.Message);
            ViewModel.SelectedFolder = ViewModel.Folders.FirstOrDefault();
            await ViewModel.LoadFoldersAsync();
            await ViewModel.LoadTasksAsync();
        }
        else ViewModel.ShowError(result.Message);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Detail is null) return;
        var name = ViewModel.Detail.Name;
        var path = ViewModel.Detail.Path;

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete task",
            Content = $"Are you sure you want to permanently delete '{name}'? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        Services.ThemeManager.ApplyToDialog(confirm);
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        var result = await Services.TaskerClient.DeleteTaskAsync(path);
        if (result.Success)
        {
            ViewModel.ShowInfo(result.Message);
            ViewModel.SelectedTask = null;
            await ViewModel.RefreshAsync();
        }
        else
        {
            ViewModel.ShowError(result.Message);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.FileTypeFilter.Add(".xml");
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var xml = await FileIO.ReadTextAsync(file);
        var name = Path.GetFileNameWithoutExtension(file.Name);
        var folder = ViewModel.SelectedFolder?.Path ?? "\\";

        // Importing an external .xml is bringing in a pre-existing definition, so preserve its
        // original <Source> (don't stamp it as created by Task Scheduler Studio), matching restore.
        var result = await Services.TaskerClient.ImportXmlAsync(folder, name, xml, stampSource: false);
        if (result.Success)
        {
            ViewModel.ShowInfo(result.Message);
            await ViewModel.RefreshAsync();
        }
        else
        {
            ViewModel.ShowError($"Import failed: {result.Message}");
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Detail is null) return;

        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("Task XML", new List<string> { ".xml" });
        picker.SuggestedFileName = ViewModel.Detail.Name;

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            var xml = await Services.TaskerClient.ExportXmlAsync(ViewModel.Detail.Path);
            await FileIO.WriteTextAsync(file, xml);
            ViewModel.ShowInfo($"Exported to {file.Name}.");
        }
        catch (Exception ex)
        {
            ViewModel.ShowError($"Export failed: {ex.Message}");
        }
    }

    private void CopyXml_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Detail is null) return;
        if (Helpers.Clip.TrySetText(ViewModel.Detail.Xml))
            ViewModel.ShowInfo("Task XML copied to clipboard.");
        else
            ViewModel.ShowError("Couldn't access the clipboard. Try again.");
    }

    private async void History_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        await ViewModel.LoadHistoryAsync();
    }
}
