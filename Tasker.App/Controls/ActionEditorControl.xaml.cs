using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Tasker_App.ViewModels;

namespace Tasker_App.Controls;

/// <summary>
/// The full editable surface for a single Exec action (program/script, arguments, working
/// directory) with a built-in file picker. Shared by the tabbed editor and the visual designer so
/// both edit an <see cref="ActionEditViewModel"/> through identical UI. Set
/// <see cref="ShowRemoveButton"/> to show the built-in remove affordance (raises
/// <see cref="RemoveRequested"/>) or provide removal in the host chrome.
/// </summary>
public sealed partial class ActionEditorControl : UserControl
{
    public ActionEditorControl()
    {
        InitializeComponent();
    }

    public ActionEditViewModel? ViewModel
    {
        get => (ActionEditViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ActionEditViewModel), typeof(ActionEditorControl),
        new PropertyMetadata(null, OnBindingSourceChanged));

    /// <summary>When true, a built-in remove button is shown that raises <see cref="RemoveRequested"/>.</summary>
    public bool ShowRemoveButton
    {
        get => (bool)GetValue(ShowRemoveButtonProperty);
        set => SetValue(ShowRemoveButtonProperty, value);
    }

    public static readonly DependencyProperty ShowRemoveButtonProperty = DependencyProperty.Register(
        nameof(ShowRemoveButton), typeof(bool), typeof(ActionEditorControl),
        new PropertyMetadata(false, OnBindingSourceChanged));

    /// <summary>Raised when the user clicks the built-in remove button.</summary>
    public event EventHandler? RemoveRequested;

    private static void OnBindingSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ActionEditorControl)d).Bindings.Update();

    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this, EventArgs.Empty);

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;

        var source = (FrameworkElement)sender;
        var picker = new FileOpenPicker(source.XamlRoot.ContentIslandEnvironment.AppWindowId)
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            ViewModel.Command = file.Path;
    }
}
