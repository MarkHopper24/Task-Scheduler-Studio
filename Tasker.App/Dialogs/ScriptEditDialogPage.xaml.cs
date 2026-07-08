using Microsoft.UI.Xaml.Controls;
using Tasker_App.Services;

namespace Tasker_App.Dialogs;

/// <summary>
/// Create/edit form for a user-saved library script, hosted as the content of a plain
/// <see cref="ContentDialog"/>. Collects title, description, category, admin/background flags, and
/// the PowerShell body, and (on the dialog's primary button) validates and produces a
/// <see cref="ScriptTemplate"/> the caller persists via <see cref="ScriptLibraryStore"/>.
/// </summary>
public sealed partial class ScriptEditDialogPage : Page
{
    private readonly string _id;

    public bool Saved { get; private set; }
    public ScriptTemplate? Result { get; private set; }

    /// <summary>Opens the form for a new script, or pre-filled from <paramref name="existing"/> to edit
    /// or duplicate it. Pass <paramref name="newId"/>=true to save as a new entry (Duplicate).</summary>
    public ScriptEditDialogPage(ScriptTemplate? existing = null, bool newId = false)
    {
        InitializeComponent();

        _id = existing is null || newId ? Guid.NewGuid().ToString("N") : existing.Id;

        if (existing is not null)
        {
            TitleBox.Text = newId ? $"{existing.Title} (copy)" : existing.Title;
            DescriptionBox.Text = existing.Description;
            CategoryCombo.SelectedItem = existing.CategoryName;
            AdminToggle.IsOn = existing.RequiresAdmin;
            BackgroundToggle.IsOn = existing.Background;
            // Prefer the raw body; for a built-in being duplicated, render its default script.
            BodyBox.Text = existing.Body ?? existing.DefaultScript();
        }
        else
        {
            CategoryCombo.SelectedItem = "System";
        }
    }

    public void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var title = TitleBox.Text.Trim();
        var body = BodyBox.Text;

        if (string.IsNullOrWhiteSpace(title))
        {
            ShowError("Enter a title for the script.");
            args.Cancel = true;
            return;
        }
        if (string.IsNullOrWhiteSpace(body))
        {
            ShowError("Enter the PowerShell script body.");
            args.Cancel = true;
            return;
        }

        var category = Enum.TryParse<ScriptCategory>(CategoryCombo.SelectedItem as string, out var c) ? c : ScriptCategory.System;

        Result = new ScriptTemplate
        {
            Id = _id,
            Title = title,
            Description = DescriptionBox.Text.Trim(),
            Category = category,
            RequiresAdmin = AdminToggle.IsOn,
            Background = BackgroundToggle.IsOn,
            Body = body,
        };
        Saved = true;
    }

    private void ShowError(string message)
    {
        ValidationBar.Message = message;
        ValidationBar.IsOpen = true;
    }
}
