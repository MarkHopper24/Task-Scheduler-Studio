using Tasker.Core;

namespace Tasker_App.ViewModels;

/// <summary>
/// Navigation payload for the visual <c>DesignerPage</c>. With no existing task it opens a blank
/// new-task canvas; with <see cref="Existing"/> set it loads that task for editing (or duplicating).
/// The library's "Create task" uses <see cref="SeedAction"/> to pre-fill the single action.
/// </summary>
public sealed record DesignerNavArgs
{
    /// <summary>The task to load for editing/duplicating, or null for a brand-new task.</summary>
    public TaskDetailDto? Existing { get; init; }

    /// <summary>When true with <see cref="Existing"/> set, loads a copy (new task) rather than editing in place.</summary>
    public bool AsDuplicate { get; init; }

    /// <summary>Folder to pre-select for a new task (ignored when editing an existing task).</summary>
    public string? DefaultFolder { get; init; }

    /// <summary>For a new task, replaces the default action with this pre-built one (e.g. a library script).</summary>
    public ActionEditViewModel? SeedAction { get; init; }

    /// <summary>Suggested name for a new task (e.g. the script title).</summary>
    public string? SeedName { get; init; }

    /// <summary>When true, a new task starts with "Run with highest privileges" on (admin scripts).</summary>
    public bool SuggestHighestPrivileges { get; init; }
}
