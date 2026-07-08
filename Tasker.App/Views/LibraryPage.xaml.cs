using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using Tasker_App.Dialogs;
using Tasker_App.Services;
using Tasker_App.ViewModels;
using Windows.Storage.Pickers;

namespace Tasker_App.Views;

/// <summary>
/// The Script Library: browse built-in and user-saved PowerShell scripts, tweak a script's friendly
/// settings (which regenerate an editable preview), copy it, create a scheduled task from it, or
/// manage your own scripts. Selecting "Create task" materializes the (possibly edited) script to a
/// .ps1 and hands a pre-built action to the visual designer so the user just picks a schedule.
/// </summary>
public sealed partial class LibraryPage : Page
{
    public LibraryPageViewModel ViewModel { get; } = new();

    private ScriptTemplate? _current;
    private readonly Dictionary<string, FrameworkElement> _fieldControls = new();

    public LibraryPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var previousId = _current?.Id;
        ViewModel.Reload();
        // Keep the selection across a reload (e.g. returning after editing a user script).
        if (previousId is not null)
            ScriptList.SelectedItem = ViewModel.Filtered.FirstOrDefault(s => s.Id == previousId);
    }

    // ---------------------------------------------------------------- filtering

    private void Category_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryCombo.SelectedItem is ComboBoxItem { Content: string text })
            ViewModel.SelectedCategory = text;
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ViewModel.SearchText = sender.Text;
    }

    private void ScriptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ShowPreview(ScriptList.SelectedItem as ScriptTemplate);

    // ---------------------------------------------------------------- preview

    private void ShowPreview(ScriptTemplate? script)
    {
        _current = script;
        if (script is null)
        {
            PreviewContent.Visibility = Visibility.Collapsed;
            PreviewEmpty.Visibility = Visibility.Visible;
            return;
        }

        PreviewEmpty.Visibility = Visibility.Collapsed;
        PreviewContent.Visibility = Visibility.Visible;

        PreviewGlyph.Glyph = script.Glyph;
        PreviewTitle.Text = script.Title;
        PreviewDescription.Text = script.Description;
        AdminHint.IsOpen = script.RequiresAdmin;

        DuplicateItem.Visibility = Visibility.Visible;
        EditItem.Visibility = script.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;
        DeleteItem.Visibility = script.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;

        BuildFields(script);
        RegenerateScript();
    }

    private void BuildFields(ScriptTemplate script)
    {
        FieldsPanel.Children.Clear();
        _fieldControls.Clear();

        ParametersSection.Visibility = script.Fields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var field in script.Fields)
        {
            var container = new StackPanel { Spacing = 4 };
            container.Children.Add(new TextBlock
            {
                Text = field.Label,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });

            FrameworkElement input = field.Kind switch
            {
                WizardFieldKind.Multiline => MakeTextBox(field, multiline: true),
                WizardFieldKind.Number => MakeNumberBox(field),
                WizardFieldKind.Choice => MakeCombo(field),
                WizardFieldKind.FolderPath => MakePicker(field, folder: true),
                WizardFieldKind.FilePath => MakePicker(field, folder: false),
                _ => MakeTextBox(field, multiline: false),
            };
            _fieldControls[field.Key] = input;
            container.Children.Add(input);

            if (!string.IsNullOrEmpty(field.Help))
            {
                container.Children.Add(new TextBlock
                {
                    Text = field.Help,
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                });
            }
            FieldsPanel.Children.Add(container);
        }
    }

    private TextBox MakeTextBox(WizardField field, bool multiline)
    {
        var tb = new TextBox
        {
            Text = field.Default,
            PlaceholderText = field.Placeholder,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Height = multiline ? 72 : double.NaN,
        };
        tb.TextChanged += (_, _) => RegenerateScript();
        return tb;
    }

    private NumberBox MakeNumberBox(WizardField field)
    {
        var nb = new NumberBox
        {
            Value = double.TryParse(field.Default, out var v) ? v : field.Min,
            Minimum = field.Min,
            Maximum = field.Max,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        nb.ValueChanged += (_, _) => RegenerateScript();
        return nb;
    }

    private ComboBox MakeCombo(WizardField field)
    {
        var cb = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var c in field.Choices ?? Array.Empty<string>()) cb.Items.Add(c);
        cb.SelectedItem = string.IsNullOrEmpty(field.Default) ? cb.Items.FirstOrDefault() : field.Default;
        if (cb.SelectedItem is null && cb.Items.Count > 0) cb.SelectedIndex = 0;
        cb.SelectionChanged += (_, _) => RegenerateScript();
        return cb;
    }

    private Grid MakePicker(WizardField field, bool folder)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new TextBox { Text = field.Default };
        box.TextChanged += (_, _) => RegenerateScript();
        Grid.SetColumn(box, 0);

        var browse = new Button { Content = "Browse\u2026", VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(browse, 1);
        browse.Click += async (_, _) =>
        {
            var path = folder ? await PickFolderAsync() : await PickFileAsync();
            if (!string.IsNullOrEmpty(path)) box.Text = path;
        };

        grid.Children.Add(box);
        grid.Children.Add(browse);
        return grid;
    }

    private void RegenerateScript()
    {
        if (_current is null) return;
        var values = new Dictionary<string, string>();
        foreach (var (key, ctrl) in _fieldControls)
        {
            values[key] = ctrl switch
            {
                TextBox tb => tb.Text,
                NumberBox nb => double.IsNaN(nb.Value) ? "0" : ((int)nb.Value).ToString(),
                ComboBox cb => cb.SelectedItem?.ToString() ?? string.Empty,
                Grid g => g.Children.OfType<TextBox>().FirstOrDefault()?.Text ?? string.Empty,
                _ => string.Empty,
            };
        }
        ScriptBox.Text = _current.BuildScript(values);
    }

    // ---------------------------------------------------------------- actions

    private void Copy_Click(object sender, RoutedEventArgs e)
        => Helpers.Clip.TrySetText(ScriptBox.Text);

    private void CreateTask_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;

        var path = ScriptMaterializer.Write(_current.Title, ScriptBox.Text);
        var (command, arguments) = ScriptMaterializer.BuildAction(path, _current.Background);

        var action = new ActionEditViewModel
        {
            Command = command,
            Arguments = arguments,
            IsExpanded = false,
        };

        Frame.Navigate(typeof(DesignerPage), new DesignerNavArgs
        {
            SeedAction = action,
            SeedName = _current.Title,
            SuggestHighestPrivileges = _current.RequiresAdmin,
        });
    }

    private async void NewScript_Click(object sender, RoutedEventArgs e)
        => await EditUserScriptAsync(new ScriptEditDialogPage(), "New script");

    private async void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        await EditUserScriptAsync(new ScriptEditDialogPage(_current, newId: true), "Duplicate script");
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _current.IsBuiltIn) return;
        await EditUserScriptAsync(new ScriptEditDialogPage(_current), "Edit script");
    }

    private async Task EditUserScriptAsync(ScriptEditDialogPage page, string title)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = page,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 760d;
        dialog.Resources["ContentDialogMaxHeight"] = 900d;
        dialog.PrimaryButtonClick += page.OnSave;

        Services.ThemeManager.ApplyToDialog(dialog);
        await dialog.ShowAsync();
        if (page.Saved && page.Result is { } saved)
        {
            var stored = ScriptLibraryStore.Save(saved);
            ViewModel.Reload();
            ScriptList.SelectedItem = ViewModel.Filtered.FirstOrDefault(s => s.Id == stored.Id);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _current.IsBuiltIn) return;
        var name = _current.Title;
        var id = _current.Id;

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete script",
            Content = $"Delete \u201C{name}\u201D from your library? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        Services.ThemeManager.ApplyToDialog(confirm);
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        ScriptLibraryStore.Delete(id);
        ViewModel.Reload();
        ShowPreview(null);
    }

    // ---------------------------------------------------------------- pickers

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<string?> PickFileAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
