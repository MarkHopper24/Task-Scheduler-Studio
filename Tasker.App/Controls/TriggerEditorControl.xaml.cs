using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tasker_App.ViewModels;

namespace Tasker_App.Controls;

/// <summary>
/// The full editable surface for a single trigger (type picker, enabled toggle, and every
/// schedule-specific field). Shared by the tabbed editor and the visual designer so both edit a
/// <see cref="TriggerEditViewModel"/> through identical UI. Hosts supply their own card chrome;
/// set <see cref="ShowRemoveButton"/> to show the built-in remove affordance (which raises
/// <see cref="RemoveRequested"/>) or leave it off and provide removal in the host chrome.
/// </summary>
public sealed partial class TriggerEditorControl : UserControl
{
    public TriggerEditorControl()
    {
        InitializeComponent();
    }

    public TriggerEditViewModel? ViewModel
    {
        get => (TriggerEditViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(TriggerEditViewModel), typeof(TriggerEditorControl),
        new PropertyMetadata(null, OnBindingSourceChanged));

    /// <summary>When true, a built-in remove button is shown that raises <see cref="RemoveRequested"/>.</summary>
    public bool ShowRemoveButton
    {
        get => (bool)GetValue(ShowRemoveButtonProperty);
        set => SetValue(ShowRemoveButtonProperty, value);
    }

    public static readonly DependencyProperty ShowRemoveButtonProperty = DependencyProperty.Register(
        nameof(ShowRemoveButton), typeof(bool), typeof(TriggerEditorControl),
        new PropertyMetadata(false, OnBindingSourceChanged));

    /// <summary>Raised when the user clicks the built-in remove button.</summary>
    public event EventHandler? RemoveRequested;

    private static void OnBindingSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TriggerEditorControl)d).Bindings.Update();

    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this, EventArgs.Empty);
}
