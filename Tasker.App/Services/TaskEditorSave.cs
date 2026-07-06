using Tasker.Core;
using Tasker_App.ViewModels;

namespace Tasker_App.Services;

/// <summary>
/// The single, shared save routine for a <see cref="TaskEditorViewModel"/>, used by both the tabbed
/// editor dialog and the visual designer so the two modes can never diverge. It validates and builds
/// the request, guards against overwriting a different task when a rename/move changes the path, then
/// registers the task and (on a rename/move) deletes the original so the edit behaves as a move.
/// </summary>
public static class TaskEditorSave
{
    /// <summary>
    /// Validates and saves the task described by <paramref name="vm"/>. On failure the reason is
    /// written to <see cref="TaskEditorViewModel.ValidationMessage"/> and the return value is either
    /// <c>null</c> (validation or a name/folder clash) or a failed <see cref="OperationResult"/>.
    /// On success returns the succeeded result. Callers are expected to have already applied any
    /// UI-only fields (e.g. the run-as password) onto the view-model.
    /// </summary>
    public static async Task<OperationResult?> SaveAsync(TaskEditorViewModel vm)
    {
        var request = vm.BuildRequest();
        if (request is null)
            return null; // BuildRequest set ValidationMessage

        // A rename/move (edit that changes name or folder) registers the task at a new path. Because
        // registration is create-or-update, saving onto a path where a DIFFERENT task already lives
        // would silently overwrite it, and we'd then delete the original too. Block that so an
        // accidental name clash can't destroy another task.
        var targetPath = CombinePath(request.Folder, request.Name);
        if (vm.IsEditMode && vm.OriginalPath is { } orig &&
            !string.Equals(orig, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            var clash = await TaskerClient.GetTaskAsync(targetPath);
            if (clash is not null)
            {
                vm.ValidationMessage = $"A task already exists at \u201C{targetPath}\u201D. Choose a different name or folder.";
                return null;
            }
        }

        var result = await TaskerClient.CreateOrUpdateAsync(request);
        if (!result.Success)
        {
            vm.ValidationMessage = result.Message;
            return result;
        }

        // Editing is a create-or-update at the (possibly new) name/folder. If the user renamed or
        // moved the task, the original still exists at its old path, so delete it so the edit behaves
        // as a move rather than leaving a duplicate behind. Best-effort: if the delete fails the new
        // task is still saved.
        if (vm.IsEditMode && vm.OriginalPath is { } original &&
            result.Path is { } newPath &&
            !string.Equals(original, newPath, StringComparison.OrdinalIgnoreCase))
        {
            await TaskerClient.DeleteTaskAsync(original);
        }

        return result;
    }

    private static string CombinePath(string folder, string name)
    {
        folder = string.IsNullOrEmpty(folder) ? "\\" : folder;
        return folder.EndsWith('\\') ? folder + name : folder + "\\" + name;
    }
}
