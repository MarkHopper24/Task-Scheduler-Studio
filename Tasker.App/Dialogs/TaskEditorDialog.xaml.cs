using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tasker.Core;
using Tasker_App.Services;
using Tasker_App.ViewModels;
using Windows.Storage.Pickers;

namespace Tasker_App.Dialogs;

/// <summary>
/// Create/edit experience for a scheduled task. Validates and saves on the primary button;
/// on success it sets <see cref="Saved"/> and <see cref="LastResult"/> for the caller to refresh.
/// </summary>
public sealed partial class TaskEditorDialog : ContentDialog
{
    public TaskEditorViewModel ViewModel { get; }

    public bool Saved { get; private set; }
    public OperationResult? LastResult { get; private set; }

    public TaskEditorDialog(TaskEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        // Size the dialog content to the available window height so its last fields are never
        // clipped behind the Save/Cancel bar. We leave headroom for the title and command area.
        var available = XamlRoot?.Size.Height ?? 0;
        if (available <= 0) return;

        const double chrome = 170; // title + button bar + padding
        var target = available - chrome;
        target = Math.Clamp(target, RootGrid.MinHeight, 900);
        RootGrid.Height = target;
    }

    private void SectionBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var selected = sender.SelectedItem;
        GeneralPanel.Visibility = selected == TabGeneral ? Visibility.Visible : Visibility.Collapsed;
        TriggersPanel.Visibility = selected == TabTriggers ? Visibility.Visible : Visibility.Collapsed;
        ActionsPanel.Visibility = selected == TabActions ? Visibility.Visible : Visibility.Collapsed;
        ConditionsPanel.Visibility = selected == TabConditions ? Visibility.Visible : Visibility.Collapsed;
        AdvancedPanel.Visibility = selected == TabAdvanced ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            ViewModel.RunAsPassword = EditorPasswordBox.Password;
            var request = ViewModel.BuildRequest();
            if (request is null)
            {
                args.Cancel = true;
                return;
            }

            var result = await TaskerClient.CreateOrUpdateAsync(request);
            if (!result.Success)
            {
                ViewModel.ValidationMessage = result.Message;
                args.Cancel = true;
                return;
            }

            Saved = true;
            LastResult = result;
        }
        finally
        {
            deferral.Complete();
        }
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

    private async void BrowseCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ActionEditViewModel action }) return;

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            action.Command = file.Path;
    }
}
