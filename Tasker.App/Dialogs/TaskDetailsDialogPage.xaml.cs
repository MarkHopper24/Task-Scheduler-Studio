using Microsoft.UI.Xaml.Controls;
using Tasker.Core;
using Tasker_App.Services;
using Tasker_App.ViewModels;

namespace Tasker_App.Dialogs;

/// <summary>
/// The final "Save task" step for the visual designer: a modal form (hosted as the content of a
/// plain <see cref="ContentDialog"/>) that collects the task's name, folder, description, and
/// run-as / advanced options, then validates and saves via the shared <see cref="TaskEditorSave"/>
/// routine. It edits the same <see cref="TaskEditorViewModel"/> the designer canvas is bound to.
/// </summary>
public sealed partial class TaskDetailsDialogPage : Page
{
    public TaskEditorViewModel ViewModel { get; }

    public bool Saved { get; private set; }
    public OperationResult? LastResult { get; private set; }

    public TaskDetailsDialogPage(TaskEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private void Password_Changed(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => ViewModel.RunAsPassword = DetailsPasswordBox.Password;

    /// <summary>Wired to the hosting dialog's primary button. Validates and saves; on failure the
    /// dialog is kept open with the reason shown in the InfoBar.</summary>
    public async void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            ViewModel.RunAsPassword = DetailsPasswordBox.Password;
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
}
