using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tasker.Core;
using Tasker_App.Services;
using Tasker_App.ViewModels;

namespace Tasker_App.Dialogs;

/// <summary>
/// Create/edit experience for a scheduled task. Hosted as the <c>Content</c> of a plain
/// <see cref="ContentDialog"/> (built by the caller, see <c>TasksPage.ShowEditorAsync</c>) rather
/// than being a ContentDialog subclass itself, so the dialog keeps the default WinUI3 chrome and
/// styling. Validates and saves on the primary button; on success it sets <see cref="Saved"/> and
/// <see cref="LastResult"/> for the caller to refresh.
/// </summary>
public sealed partial class TaskEditorDialogPage : Page
{
    public TaskEditorViewModel ViewModel { get; }

    public bool Saved { get; private set; }
    public OperationResult? LastResult { get; private set; }

    public TaskEditorDialogPage(TaskEditorViewModel viewModel)
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

    public async void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            ViewModel.RunAsPassword = EditorPasswordBox.Password;
            var result = await TaskEditorSave.SaveAsync(ViewModel);
            if (result is null || !result.Success)
            {
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

    private void TriggerCard_RemoveRequested(object? sender, EventArgs e)
    {
        if (sender is Controls.TriggerEditorControl { ViewModel: { } trigger })
            ViewModel.Triggers.Remove(trigger);
    }

    private void ActionCard_RemoveRequested(object? sender, EventArgs e)
    {
        if (sender is Controls.ActionEditorControl { ViewModel: { } action })
            ViewModel.Actions.Remove(action);
    }
}
