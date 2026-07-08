using Microsoft.UI.Xaml.Controls;

namespace Tasker_App.Dialogs;

/// <summary>
/// Lets the user pick a destination folder to move one or more tasks into. Hosted as the
/// <c>Content</c> of a plain <see cref="ContentDialog"/> built by the caller (see
/// <c>TasksPage.ShowMoveDialogAsync</c>), which wires <see cref="OnMove"/> to its PrimaryButtonClick.
/// The actual move (export XML / import at destination / delete original) happens in the caller via
/// <c>TasksPageViewModel.MoveTaskAsync</c> once <see cref="Moved"/> is true.
/// </summary>
public sealed partial class MoveToFolderDialogPage : Page
{
    private readonly string _currentFolder;

    public bool Moved { get; private set; }
    public string DestinationFolder { get; private set; } = "\\";

    public MoveToFolderDialogPage(int taskCount, string currentFolder)
    {
        _currentFolder = string.IsNullOrEmpty(currentFolder) ? "\\" : currentFolder;
        InitializeComponent();

        MessageText.Text = taskCount == 1
            ? "Choose a destination folder for this task."
            : $"Choose a destination folder for these {taskCount} tasks.";
        FolderCombo.Text = _currentFolder;
    }

    public void SetFolders(IEnumerable<string> folders) => FolderCombo.ItemsSource = folders.ToList();

    public void OnMove(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var dest = (FolderCombo.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(dest)) dest = "\\";
        if (!dest.StartsWith('\\')) dest = "\\" + dest;

        if (string.Equals(dest, _currentFolder, StringComparison.OrdinalIgnoreCase))
        {
            ErrorBar.Message = "Choose a different folder than the current one.";
            ErrorBar.IsOpen = true;
            args.Cancel = true;
            return;
        }

        DestinationFolder = dest;
        Moved = true;
    }
}
