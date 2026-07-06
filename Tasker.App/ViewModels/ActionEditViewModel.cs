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

    /// <summary>Designer-only: whether this action's card is expanded. New actions start expanded;
    /// actions loaded from an existing task (see <see cref="FromDto"/>) start collapsed to a summary.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>A live, plain-language one-line recap of this action for the designer card header
    /// (e.g. "Run backup.ps1", "Launch notepad.exe"). Recomputed on any field change.</summary>
    public string Summary => BuildSummary();

    /// <summary>Segoe Fluent icon glyph for the designer action card.</summary>
    public string Glyph => "\uE756"; // Command prompt

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != nameof(Summary))
            OnPropertyChanged(nameof(Summary));
    }

    private string BuildSummary()
    {
        var cmd = Command.Trim();
        if (cmd.Length == 0) return "No program set yet";

        string leaf;
        try { leaf = System.IO.Path.GetFileName(cmd.Trim('"')); }
        catch { leaf = cmd; }
        if (string.IsNullOrEmpty(leaf)) leaf = cmd;

        var args = Arguments.Trim();
        if (args.Length > 0)
        {
            if (args.Length > 48) args = args[..45] + "\u2026";
            return $"Run {leaf} {args}";
        }
        return $"Run {leaf}";
    }

    public ActionEditViewModel() { }

    public static ActionEditViewModel FromDto(ActionDto dto) => new()
    {
        Command = dto.Command,
        Arguments = dto.Arguments,
        WorkingDirectory = dto.WorkingDirectory,
        IsExpanded = false,
    };

    public ActionDto ToDto() => new()
    {
        Kind = ActionKind.Exec,
        Command = Command.Trim(),
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory.Trim(),
    };
}
