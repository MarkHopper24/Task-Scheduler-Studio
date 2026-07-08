using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tasker_App.Dialogs;
using Tasker_App.Services;
using Tasker_App.ViewModels;

namespace Tasker_App.Views;

/// <summary>
/// A full-page, Power Automate style visual designer for a scheduled task: a vertical flow of cards
/// (triggers, conditions, actions, task details) connected by "+" insert nodes. It is an alternative
/// mode to the tabbed <see cref="TaskEditorDialogPage"/> and edits the very same
/// <see cref="TaskEditorViewModel"/>, so both modes share validation and the one save routine
/// (<see cref="TaskEditorSave"/>) and can never diverge.
/// </summary>
public sealed partial class DesignerPage : Page
{
    public TaskEditorViewModel ViewModel { get; private set; } = new();

    public DesignerPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var args = e.Parameter as DesignerNavArgs ?? new DesignerNavArgs();
        var editorVm = new TaskEditorViewModel(args.Existing, args.AsDuplicate);
        if (!editorVm.IsEditMode && !string.IsNullOrEmpty(args.DefaultFolder))
            editorVm.Folder = args.DefaultFolder!;

        // A new task seeded from the script library: use the pre-built action, name, and admin hint.
        if (!editorVm.IsEditMode && args.SeedAction is not null)
        {
            editorVm.Actions.Clear();
            editorVm.Actions.Add(args.SeedAction);
            if (!string.IsNullOrWhiteSpace(args.SeedName) && string.IsNullOrWhiteSpace(editorVm.TaskName))
                editorVm.TaskName = args.SeedName!;
            if (args.SuggestHighestPrivileges)
                editorVm.RunWithHighestPrivileges = true;
        }

        ViewModel = editorVm;
        Bindings.Update();

        try { editorVm.SetFolders(await TaskerClient.GetFolderPathsAsync()); }
        catch { /* folder suggestions are best-effort */ }
    }

    // ---------------------------------------------------------------- add / remove

    private void AddTrigger_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        foreach (var opt in TriggerEditViewModel.KindOptions)
        {
            var kind = opt.Value;
            var item = new MenuFlyoutItem { Text = opt.Label };
            item.Click += (_, _) => ViewModel.Triggers.Add(new TriggerEditViewModel { Kind = kind, IsExpanded = true });
            flyout.Items.Add(item);
        }
        flyout.ShowAt((FrameworkElement)sender);
    }

    private void AddAction_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();

        void Add(string text, Action<ActionEditViewModel> configure)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) =>
            {
                var action = new ActionEditViewModel { IsExpanded = true };
                configure(action);
                ViewModel.Actions.Add(action);
            };
            flyout.Items.Add(item);
        }

        Add("Run a program or script", _ => { });
        Add("Run a PowerShell script", a =>
        {
            a.Command = TaskRecipes.PowerShell;
            a.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"\"";
        });
        Add("Open a website", a =>
        {
            a.Command = @"C:\Windows\System32\cmd.exe";
            a.Arguments = "/c start \"\" \"https://\"";
        });
        Add("Open a file or folder", a =>
        {
            a.Command = @"C:\Windows\explorer.exe";
            a.Arguments = "\"\"";
        });

        flyout.Items.Add(new MenuFlyoutSeparator());
        var libraryItem = new MenuFlyoutItem
        {
            Text = "From script library\u2026",
            Icon = new FontIcon { Glyph = "\uE82D" },
        };
        libraryItem.Click += async (_, _) => await AddFromLibraryAsync();
        flyout.Items.Add(libraryItem);

        flyout.ShowAt((FrameworkElement)sender);
    }

    /// <summary>Opens the script-library picker; the chosen script is materialized to a .ps1 and
    /// added as an action. Admin scripts also switch on "Run with highest privileges".</summary>
    private async Task AddFromLibraryAsync()
    {
        var page = new Dialogs.ScriptPickerDialogPage();
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add a script",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = page,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 700d;

        Services.ThemeManager.ApplyToDialog(dialog);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || page.Chosen is not { } script)
            return;

        var path = ScriptMaterializer.Write(script.Title, script.DefaultScript());
        var (command, arguments) = ScriptMaterializer.BuildAction(path, script.Background);
        ViewModel.Actions.Add(new ActionEditViewModel { Command = command, Arguments = arguments, IsExpanded = false });
        if (script.RequiresAdmin)
            ViewModel.RunWithHighestPrivileges = true;
    }

    private void RemoveTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TriggerEditViewModel trigger })
            ViewModel.Triggers.Remove(trigger);
    }

    private void RemoveAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActionEditViewModel action })
            ViewModel.Actions.Remove(action);
    }

    // ---------------------------------------------------------------- commands

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        // The task details (name, folder, run-as, advanced) are collected in a final modal so the
        // canvas stays focused on the flow itself. The modal edits the same view-model and saves
        // through the shared routine; only a successful save returns to the task list.
        var page = new TaskDetailsDialogPage(ViewModel);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Save task",
            PrimaryButtonText = "Save",
            CloseButtonText = "Back",
            DefaultButton = ContentDialogButton.Primary,
            Content = page,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720d;
        dialog.Resources["ContentDialogMaxHeight"] = 900d;
        dialog.PrimaryButtonClick += page.OnSave;

        Services.ThemeManager.ApplyToDialog(dialog);
        await dialog.ShowAsync();
        if (page.Saved)
            Frame.Navigate(typeof(TasksPage));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
        else Frame.Navigate(typeof(TasksPage));
    }

    /// <summary>Hands the current in-progress task off to the classic tabbed editor over the same
    /// live view-model, so the two modes stay in sync. If the user saves there, we return to the
    /// task list; otherwise the canvas reflects any edits made in the dialog.</summary>
    private async void OpenFormEditor_Click(object sender, RoutedEventArgs e)
    {
        var page = new TaskEditorDialogPage(ViewModel);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ViewModel.Title,
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
            Frame.Navigate(typeof(TasksPage));
        else
            Bindings.Update();
    }
}
