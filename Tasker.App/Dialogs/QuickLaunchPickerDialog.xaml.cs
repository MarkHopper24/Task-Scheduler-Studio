using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tasker_App.Services;
using Tasker_App.ViewModels;

namespace Tasker_App.Dialogs;

/// <summary>
/// Lets the user pick which enabled tasks to pin to Quick Launch. Loads all enabled tasks,
/// pre-checks the currently-pinned ones, and exposes the chosen paths via <see cref="SelectedPaths"/>.
/// </summary>
public sealed partial class QuickLaunchPickerDialog : ContentDialog
{
    private readonly HashSet<string> _initiallyPinned;

    public ObservableCollection<PinnableTask> AllTasks { get; } = new();
    public ObservableCollection<PinnableTask> FilteredTasks { get; } = new();

    /// <summary>The task paths the user chose to pin (only valid after a "Save" result).</summary>
    public IReadOnlyList<string> SelectedPaths { get; private set; } = Array.Empty<string>();

    public QuickLaunchPickerDialog(IEnumerable<string> currentlyPinned)
    {
        _initiallyPinned = new HashSet<string>(currentlyPinned, StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
        Loaded += OnLoaded;
        PrimaryButtonClick += OnSave;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var onlyTasker = AppSettings.OnlyTaskerTasks;
            var all = await TaskerClient.ListTasksAsync("\\", recursive: true);
            foreach (var t in all
                .Where(t => t.Enabled && (!onlyTasker || t.CreatedByTasker))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                AllTasks.Add(new PinnableTask
                {
                    Name = t.Name,
                    Folder = t.Folder,
                    Path = t.Path,
                    IsPinned = _initiallyPinned.Contains(t.Path),
                });
            }
            ApplyFilter(string.Empty);
        }
        catch
        {
            // Best-effort: if the Task Scheduler can't be read, show the empty state rather than
            // letting the exception escape this async void handler and crash the app.
        }
        finally
        {
            LoadingRing.IsActive = false;
            EmptyText.Visibility = AllTasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void FilterBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ApplyFilter(sender.Text);
    }

    private void ApplyFilter(string query)
    {
        query = (query ?? string.Empty).Trim();
        FilteredTasks.Clear();
        foreach (var t in AllTasks)
        {
            if (query.Length == 0
                || t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || t.Folder.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredTasks.Add(t);
            }
        }
    }

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Honour every checkbox the user toggled, even ones currently filtered out of view.
        SelectedPaths = AllTasks.Where(t => t.IsPinned).Select(t => t.Path).ToList();
    }
}
