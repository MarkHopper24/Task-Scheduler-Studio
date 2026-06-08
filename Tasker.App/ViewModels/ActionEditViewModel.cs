using CommunityToolkit.Mvvm.ComponentModel;
using Tasker.Core;

namespace Tasker_App.ViewModels;

/// <summary>Editable view-model for a single Exec action. Maps to/from <see cref="ActionDto"/>.</summary>
public partial class ActionEditViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Command { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Arguments { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WorkingDirectory { get; set; } = string.Empty;

    public ActionEditViewModel() { }

    public static ActionEditViewModel FromDto(ActionDto dto) => new()
    {
        Command = dto.Command,
        Arguments = dto.Arguments,
        WorkingDirectory = dto.WorkingDirectory,
    };

    public ActionDto ToDto() => new()
    {
        Kind = ActionKind.Exec,
        Command = Command.Trim(),
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory.Trim(),
    };
}
