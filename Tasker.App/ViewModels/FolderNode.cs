using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tasker.Core;

namespace Tasker_App.ViewModels;

/// <summary>An observable node in the folder TreeView.</summary>
public partial class FolderNode : ObservableObject
{
    public string Name { get; }
    public string Path { get; }
    public int TaskCount { get; }
    public ObservableCollection<FolderNode> Children { get; } = new();

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public string Glyph => Path == "\\" ? "\uE80F" : "\uE8B7"; // Home / Folder

    public FolderNode(TaskFolderDto dto, bool expand = false,
        HashSet<string>? visiblePaths = null,
        IReadOnlyDictionary<string, int>? taskerCounts = null)
    {
        Name = dto.Name;
        Path = dto.Path;
        // When filtering to Windows Task Studio tasks, show this folder's direct Tasker-task count
        // (0 if it only contains them in descendants); otherwise show the real total.
        TaskCount = taskerCounts is null
            ? dto.TaskCount
            : (taskerCounts.TryGetValue(dto.Path, out var c) ? c : 0);
        foreach (var child in dto.Children)
        {
            // While filtering, skip folders whose subtree has no Windows Task Studio tasks.
            if (visiblePaths is not null && !visiblePaths.Contains(child.Path)) continue;
            Children.Add(new FolderNode(child, false, visiblePaths, taskerCounts));
        }
        // Auto-expand kept branches while filtering so the matching tasks are actually visible.
        IsExpanded = expand || (visiblePaths is not null && Children.Count > 0);
    }
}
