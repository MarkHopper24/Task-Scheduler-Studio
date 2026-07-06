using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tasker.Core;
using Tasker_App.Services;
using Tasker_App.ViewModels;
using Windows.Storage.Pickers;

namespace Tasker_App.Dialogs;

/// <summary>
/// A friendly, step-by-step task creation wizard for non-technical users. Deterministic and
/// template-driven (no AI): the user picks a ready-made recipe, fills in plain-language fields,
/// chooses a simple schedule, reviews a plain-English summary, and the wizard builds the task.
/// Hosted as the <c>Content</c> of a plain <see cref="ContentDialog"/> built by the caller (see
/// <c>TasksPage.GuidedSetup_Click</c>); call <see cref="Attach"/> once, right after construction,
/// so step navigation can drive that dialog's button bar.
/// </summary>
public sealed partial class WizardDialogPage : Page
{
    public WizardViewModel ViewModel { get; } = new();

    public bool Created { get; private set; }
    public OperationResult? LastResult { get; private set; }

    private ContentDialog? _dialog;
    private int _step;                                  // 0 choose, 1 details, 2 schedule, 3 review
    private readonly Dictionary<string, FrameworkElement> _fieldControls = new();
    private TaskRecipe? _fieldsBuiltFor;                 // recipe the current field controls belong to
    private static readonly string[] StepTitles =
        { "Choose what to do", "Fill in the details", "Choose when it runs", "Review and create" };

    public WizardDialogPage(string? defaultFolder = null)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(defaultFolder)) ViewModel.Folder = defaultFolder!;
    }

    public void SetFolders(IEnumerable<string> folders)
    {
        WizFolder.ItemsSource = folders.ToList();
    }

    /// <summary>Wires this page to its hosting ContentDialog so step navigation can drive the
    /// dialog's title/button bar, then renders the initial step. Call once, before ShowAsync.</summary>
    public void Attach(ContentDialog dialog)
    {
        _dialog = dialog;
        UpdateStep();
    }

    // ---------------------------------------------------------------- step flow

    private void UpdateStep()
    {
        StepChoose.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        StepDetails.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        StepSchedule.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        StepReview.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;

        StepLabel.Text = $"Step {_step + 1} of 4: {StepTitles[_step]}";
        StepProgress.Value = _step + 1;

        if (_dialog is null) return;
        _dialog.SecondaryButtonText = _step == 0 ? string.Empty : "Back";
        _dialog.IsSecondaryButtonEnabled = _step > 0;
        _dialog.PrimaryButtonText = _step == 3 ? "Create task" : "Next";
        _dialog.IsPrimaryButtonEnabled = !(_step == 0 && ViewModel.SelectedRecipe is null);
    }

    private void Recipe_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TaskRecipe recipe)
        {
            ViewModel.SelectedRecipe = recipe;
            RecipeGrid.SelectedItem = recipe;
            // Picking a card advances straight into its details.
            GoToDetails();
            _step = 1;
            UpdateStep();
        }
    }

    public void OnSecondary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // never close on Back
        if (_step > 0) { _step--; UpdateStep(); }
    }

    public async void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Advance through steps; only the final step actually creates and closes.
        if (_step < 3)
        {
            args.Cancel = true;

            if (_step == 0)
            {
                if (ViewModel.SelectedRecipe is null) return;
                GoToDetails();
            }
            else if (_step == 1)
            {
                CaptureFields();
            }
            else if (_step == 2)
            {
                CaptureFields();
                EnterReview();
            }

            _step++;
            UpdateStep();
            return;
        }

        // Final step: build + create.
        var deferral = args.GetDeferral();
        try
        {
            CaptureFields();
            var request = ViewModel.BuildRequest(out var error);
            if (request is null)
            {
                WizError.Message = error ?? "Please complete the form.";
                WizError.IsOpen = true;
                args.Cancel = true;
                return;
            }

            var result = await TaskerClient.CreateOrUpdateAsync(request);
            if (!result.Success)
            {
                WizError.Message = result.Message;
                WizError.IsOpen = true;
                args.Cancel = true;
                return;
            }

            Created = true;
            LastResult = result;
        }
        finally
        {
            deferral.Complete();
        }
    }

    // ---------------------------------------------------------------- details (dynamic)

    private void GoToDetails()
    {
        var recipe = ViewModel.SelectedRecipe!;
        DetailGlyph.Glyph = recipe.Glyph;
        DetailTitle.Text = recipe.Title;
        DetailDescription.Text = recipe.Description;

        // Preserve the user's entries if the fields are already built for this recipe (e.g. they
        // pressed Back to step 0 then Next again). Only rebuild when the recipe actually changed,
        // otherwise the controls would be recreated with their defaults and typed input lost.
        if (ReferenceEquals(_fieldsBuiltFor, recipe) && _fieldControls.Count > 0)
            return;
        _fieldsBuiltFor = recipe;

        FieldsPanel.Children.Clear();
        _fieldControls.Clear();

        foreach (var field in recipe.Fields)
        {
            var label = new TextBlock { Text = field.Label, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
            var container = new StackPanel { Spacing = 4 };
            container.Children.Add(label);

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
            Height = multiline ? 90 : double.NaN,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(tb, "wizf_" + field.Key);
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
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(nb, "wizf_" + field.Key);
        return nb;
    }

    private ComboBox MakeCombo(WizardField field)
    {
        var cb = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var c in field.Choices ?? Array.Empty<string>()) cb.Items.Add(c);
        cb.SelectedItem = string.IsNullOrEmpty(field.Default) ? cb.Items.FirstOrDefault() : field.Default;
        if (cb.SelectedItem is null && cb.Items.Count > 0) cb.SelectedIndex = 0;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(cb, "wizf_" + field.Key);
        return cb;
    }

    private Grid MakePicker(WizardField field, bool folder)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnSpacing = 8;

        var box = new TextBox { Text = field.Default, PlaceholderText = folder ? "Choose a folder\u2026" : "Choose a file\u2026", IsReadOnly = false };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "wizf_" + field.Key);
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

    private void CaptureFields()
    {
        if (ViewModel.SelectedRecipe is null) return;
        foreach (var (key, ctrl) in _fieldControls)
        {
            string value = ctrl switch
            {
                TextBox tb => tb.Text,
                NumberBox nb => double.IsNaN(nb.Value) ? "0" : ((int)nb.Value).ToString(),
                ComboBox cb => cb.SelectedItem?.ToString() ?? string.Empty,
                Grid g => (g.Children.OfType<TextBox>().FirstOrDefault())?.Text ?? string.Empty,
                _ => string.Empty,
            };
            ViewModel.FieldValues[key] = value;
        }
    }

    private void EnterReview()
    {
        var result = ViewModel.BuildResult();
        ReviewWhat.Text = string.IsNullOrEmpty(result.PlainSummary) ? result.Description : result.PlainSummary;
        if (!string.IsNullOrEmpty(result.Warning))
        {
            ReviewWarning.Message = result.Warning;
            ReviewWarning.IsOpen = true;
        }
        else
        {
            ReviewWarning.IsOpen = false;
        }
        WizError.IsOpen = false;
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
