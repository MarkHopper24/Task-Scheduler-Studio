using System.Collections.Specialized;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Tasker_App.ViewModels;
using Windows.System;

namespace Tasker_App.Views;

public sealed partial class AssistantPage : Page
{
    public AssistantPageViewModel ViewModel { get; } = new();

    public AssistantPage()
    {
        InitializeComponent();
        ViewModel.Messages.CollectionChanged += OnMessagesChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAuthStatusAsync();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            TranscriptScroller.UpdateLayout();
            TranscriptScroller.ChangeView(null, TranscriptScroller.ScrollableHeight, null);
        });
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;

        var shift = (Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        if (shift) return;

        e.Handled = true;
        if (ViewModel.SendCommand.CanExecute(null))
            ViewModel.SendCommand.Execute(null);
    }

    private async void SaveToken_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.TokenInput = TokenBox.Password;
        TokenBox.Password = string.Empty;
        await ViewModel.SaveTokenCommand.ExecuteAsync(null);
    }

    private bool _modelsRequested;

    private void ModelCombo_DropDownOpened(object? sender, object e)
    {
        // Lazily fetch the model list the first time the user opens the dropdown, so we don't
        // start the Copilot CLI (and possibly prompt sign-in) unless they actually want to pick one.
        if (_modelsRequested) return;
        _modelsRequested = true;
        if (ViewModel.LoadModelsCommand.CanExecute(null))
            ViewModel.LoadModelsCommand.Execute(null);
    }

    private void Suggestion_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.FrameworkElement { DataContext: AssistantSuggestion suggestion })
        {
            ViewModel.InputText = suggestion.Prompt;
            InputBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        }
    }
}
