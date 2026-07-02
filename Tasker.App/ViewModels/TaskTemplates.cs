using Tasker.Core;

namespace Tasker_App.ViewModels;

public sealed record TaskTemplate(string Name, string Description, Func<TaskEditorViewModel> Create);

/// <summary>Ready-made starting points for the "New task" menu.</summary>
public static class TaskTemplates
{
    public static IReadOnlyList<TaskTemplate> All { get; } = new[]
    {
        new TaskTemplate("Run a program daily", "Runs a program every day at a set time.",
            () => Build("New daily task", TriggerKind.Daily, "C:\\Windows\\System32\\notepad.exe")),

        new TaskTemplate("Run at log on", "Runs when you sign in.",
            () => Build("New logon task", TriggerKind.AtLogOn, "C:\\Windows\\System32\\notepad.exe")),

        new TaskTemplate("Run at startup", "Runs when Windows starts.",
            () => Build("New startup task", TriggerKind.AtStartup, "C:\\Windows\\System32\\notepad.exe")),

        new TaskTemplate("Run a PowerShell script weekly", "Runs a .ps1 script on chosen weekdays.",
            () => BuildPowerShell()),
    };

    private static TaskEditorViewModel Build(string name, TriggerKind kind, string command)
    {
        var vm = new TaskEditorViewModel();
        vm.TaskName = name;
        vm.Triggers.Clear();
        vm.Triggers.Add(new TriggerEditViewModel { Kind = kind });
        vm.Actions.Clear();
        vm.Actions.Add(new ActionEditViewModel { Command = command });
        return vm;
    }

    private static TaskEditorViewModel BuildPowerShell()
    {
        var vm = new TaskEditorViewModel();
        vm.TaskName = "New PowerShell task";
        vm.Triggers.Clear();
        var trigger = new TriggerEditViewModel { Kind = TriggerKind.Weekly, Monday = true, Wednesday = true, Friday = true };
        vm.Triggers.Add(trigger);
        vm.Actions.Clear();
        vm.Actions.Add(new ActionEditViewModel
        {
            Command = "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"C:\\Scripts\\script.ps1\"",
        });
        return vm;
    }
}
