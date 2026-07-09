using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tasker_App.Services;
using Tasker_App.ViewModels;

namespace Tasker_App.Dialogs;

/// <summary>
/// A picker (hosted in a plain <see cref="ContentDialog"/>) that lets the designer's "Add action"
/// flow choose a script from the library. The caller reads <see cref="Chosen"/> after the dialog's
/// primary button, materializes it, and adds the resulting action.
/// </summary>
public sealed partial class ScriptPickerDialogPage : Page
{
    public LibraryPageViewModel ViewModel { get; } = new();

    /// <summary>The script the user selected (null if none).</summary>
    public ScriptTemplate? Chosen { get; private set; }

    public ScriptPickerDialogPage()
    {
        InitializeComponent();
        ViewModel.Reload();
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ViewModel.SearchText = sender.Text;
    }

    private void ScriptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => Chosen = ScriptList.SelectedItem as ScriptTemplate;
}
