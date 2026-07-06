namespace Tasker_App.Services;

public enum WizardFieldKind { Text, Multiline, Number, FolderPath, FilePath, Url, Choice }

/// <summary>One friendly input shown in the wizard (no command-line knowledge required).</summary>
public sealed class WizardField
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string Help { get; init; } = string.Empty;
    public WizardFieldKind Kind { get; init; } = WizardFieldKind.Text;
    public string Default { get; init; } = string.Empty;
    public string Placeholder { get; init; } = string.Empty;
    public int Min { get; init; } = 0;
    public int Max { get; init; } = 3650;
    public IReadOnlyList<string>? Choices { get; init; }
}

/// <summary>The concrete program/command a recipe produces from the user's answers.</summary>
public sealed class RecipeResult
{
    public string Command { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SuggestedName { get; init; } = "My task";
    /// <summary>Plain-English recap of what the task will do, shown on the review step.</summary>
    public string PlainSummary { get; init; } = string.Empty;
    /// <summary>Optional caution to surface in the review (e.g. for deletion recipes).</summary>
    public string? Warning { get; init; }
    public string? Validation { get; init; }
}

/// <summary>A ready-made, non-technical task recipe with friendly fields and a builder.</summary>
public sealed class TaskRecipe
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public IReadOnlyList<WizardField> Fields { get; init; } = Array.Empty<WizardField>();
    public required Func<IReadOnlyDictionary<string, string>, RecipeResult> Build { get; init; }
}

/// <summary>
/// Curated catalog of common things a non-technical user might want to schedule, each implemented
/// with safe, ready-made commands (often PowerShell) so the user never has to know arguments.
/// </summary>
public static class TaskRecipes
{
    public const string PowerShell = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";

    public static IReadOnlyList<TaskRecipe> All { get; } = new[]
    {
        ReminderPopup(),
        OpenWebsite(),
        OpenApp(),
        OpenFile(),
        CleanOldFiles(),
        BackupFolder(),
        PowerAction(),
        RunPowerShellScript(),
        RunCopilotPrompt(),
    };

    public static TaskRecipe? ById(string id) => All.FirstOrDefault(r => r.Id == id);

    // ---------------------------------------------------------------- recipes

    private static TaskRecipe ReminderPopup() => new()
    {
        Id = "reminder",
        Title = "Show a reminder pop-up",
        Description = "Pop up a message on your screen, great for recurring reminders.",
        Glyph = "\uE7E7", // Message
        Fields = new[]
        {
            new WizardField { Key = "title", Label = "Reminder title", Default = "Reminder", Placeholder = "Reminder" },
            new WizardField { Key = "message", Label = "Message to show", Kind = WizardFieldKind.Multiline,
                Placeholder = "Time to stand up and stretch!", Help = "This is the text that pops up on screen." },
        },
        Build = f =>
        {
            var title = Ps(f.GetValueOrDefault("title", "Reminder"));
            var msg = Ps(f.GetValueOrDefault("message", ""));
            var script = $"Add-Type -AssemblyName PresentationFramework; [System.Windows.MessageBox]::Show('{msg}','{title}')";
            return new RecipeResult
            {
                Command = PowerShell,
                // -EncodedCommand (base64 UTF-16LE) so a title/message containing a double-quote
                // can't break the outer -Command "…" quoting and silently fail the task at runtime.
                Arguments = $"-NoProfile -WindowStyle Hidden -EncodedCommand {EncodePs(script)}",
                Description = "Shows a reminder pop-up message.",
                SuggestedName = "Reminder",
                PlainSummary = $"Show a pop-up titled \u201C{f.GetValueOrDefault("title", "Reminder")}\u201D saying \u201C{f.GetValueOrDefault("message", "")}\u201D.",
                Validation = string.IsNullOrWhiteSpace(f.GetValueOrDefault("message")) ? "Enter the message to show." : null,
            };
        },
    };

    private static TaskRecipe OpenWebsite() => new()
    {
        Id = "website",
        Title = "Open a website",
        Description = "Open a web page in your default browser on a schedule.",
        Glyph = "\uE774", // Globe
        Fields = new[]
        {
            new WizardField { Key = "url", Label = "Website address", Kind = WizardFieldKind.Url,
                Placeholder = "https://www.example.com", Help = "The page to open. \u201Chttps://\u201D is added automatically if you leave it out." },
        },
        Build = f =>
        {
            var raw = (f.GetValueOrDefault("url", "") ?? "").Trim().Replace("\"", "");
            var url = raw.Length == 0 ? "" : (raw.Contains("://") ? raw : "https://" + raw);
            return new RecipeResult
            {
                Command = @"C:\Windows\System32\cmd.exe",
                Arguments = $"/c start \"\" \"{url}\"",
                Description = $"Opens {url} in the default browser.",
                SuggestedName = "Open website",
                PlainSummary = $"Open {url} in your default browser.",
                Validation = url.Length == 0 ? "Enter a website address." : null,
            };
        },
    };

    private static TaskRecipe OpenApp() => new()
    {
        Id = "app",
        Title = "Open an app or program",
        Description = "Launch a program (pick its .exe) on a schedule.",
        Glyph = "\uE71D", // AllApps
        Fields = new[]
        {
            new WizardField { Key = "path", Label = "Program", Kind = WizardFieldKind.FilePath,
                Help = "Use Browse to pick the program you want to launch." },
        },
        Build = f =>
        {
            var path = (f.GetValueOrDefault("path", "") ?? "").Trim();
            var name = SafeFileName(path);
            return new RecipeResult
            {
                Command = path,
                Description = $"Launches {name}.",
                SuggestedName = name.Length > 0 ? $"Open {name}" : "Open app",
                PlainSummary = $"Launch {(name.Length > 0 ? name : "the selected program")}.",
                Validation = path.Length == 0 ? "Pick a program to open." : null,
            };
        },
    };

    private static TaskRecipe OpenFile() => new()
    {
        Id = "file",
        Title = "Open a file or document",
        Description = "Open a document, image, or any file with its default app.",
        Glyph = "\uE8A5", // Document
        Fields = new[]
        {
            new WizardField { Key = "path", Label = "File", Kind = WizardFieldKind.FilePath,
                Help = "Use Browse to pick the file. It opens in whatever app normally handles it." },
        },
        Build = f =>
        {
            var path = (f.GetValueOrDefault("path", "") ?? "").Trim().Replace("\"", "");
            var name = SafeFileName(path);
            return new RecipeResult
            {
                Command = @"C:\Windows\explorer.exe",
                Arguments = $"\"{path}\"",
                Description = $"Opens {name} with its default app.",
                SuggestedName = name.Length > 0 ? $"Open {name}" : "Open file",
                PlainSummary = $"Open {(name.Length > 0 ? name : "the selected file")} with its default app.",
                Validation = path.Length == 0 ? "Pick a file to open." : null,
            };
        },
    };

    private static TaskRecipe CleanOldFiles() => new()
    {
        Id = "cleanup",
        Title = "Clean up old files",
        Description = "Move files older than a chosen age to the Recycle Bin (safe: nothing is permanently deleted).",
        Glyph = "\uE74D", // Delete
        Fields = new[]
        {
            new WizardField { Key = "folder", Label = "Folder to tidy", Kind = WizardFieldKind.FolderPath,
                Help = "For example your Downloads folder. Only files directly in this folder are affected." },
            new WizardField { Key = "days", Label = "Older than (days)", Kind = WizardFieldKind.Number,
                Default = "30", Min = 1, Max = 3650, Help = "Files last changed more than this many days ago are tidied away." },
        },
        Build = f =>
        {
            var folder = Ps((f.GetValueOrDefault("folder", "") ?? "").Trim());
            var folderRaw = (f.GetValueOrDefault("folder", "") ?? "").Trim();
            var days = ParseInt(f.GetValueOrDefault("days", "30"), 30);
            var script =
                "Add-Type -AssemblyName Microsoft.VisualBasic; " +
                $"Get-ChildItem -LiteralPath '{folder}' -File | " +
                $"Where-Object {{ $_.LastWriteTime -lt (Get-Date).AddDays(-{days}) }} | " +
                "ForEach-Object { [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($_.FullName,'OnlyErrorDialogs','SendToRecycleBin') }";
            return new RecipeResult
            {
                Command = PowerShell,
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                Description = $"Moves files older than {days} days in {folderRaw} to the Recycle Bin.",
                SuggestedName = "Clean up old files",
                PlainSummary = $"Move files older than {days} days in \u201C{folderRaw}\u201D to the Recycle Bin.",
                Warning = "Affected files are sent to the Recycle Bin (recoverable), not permanently deleted. Only files directly in the folder are tidied; subfolders are left alone.",
                Validation = folderRaw.Length == 0 ? "Pick the folder to tidy." : null,
            };
        },
    };

    private static TaskRecipe BackupFolder() => new()
    {
        Id = "backup",
        Title = "Back up a folder",
        Description = "Copy a folder to another location, into a dated backup folder.",
        Glyph = "\uE8C8", // Copy
        Fields = new[]
        {
            new WizardField { Key = "source", Label = "Folder to back up", Kind = WizardFieldKind.FolderPath },
            new WizardField { Key = "dest", Label = "Where to put the backup", Kind = WizardFieldKind.FolderPath,
                Help = "A dated subfolder is created here each time, so backups don't overwrite each other." },
        },
        Build = f =>
        {
            var srcRaw = (f.GetValueOrDefault("source", "") ?? "").Trim();
            var destRaw = (f.GetValueOrDefault("dest", "") ?? "").Trim();
            var src = Ps(srcRaw);
            var dest = Ps(destRaw);
            var leaf = SafeFileName(srcRaw);
            var leafSafe = string.IsNullOrEmpty(leaf) ? "Backup" : leaf;
            var script =
                "$ts = Get-Date -Format 'yyyy-MM-dd_HHmm'; " +
                $"$target = Join-Path '{dest}' ('{Ps(leafSafe)}_' + $ts); " +
                $"Copy-Item -LiteralPath '{src}' -Destination $target -Recurse -Force";
            return new RecipeResult
            {
                Command = PowerShell,
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                Description = $"Backs up {srcRaw} into {destRaw}.",
                SuggestedName = $"Back up {leafSafe}",
                PlainSummary = $"Copy \u201C{srcRaw}\u201D into a dated backup folder under \u201C{destRaw}\u201D.",
                Validation = srcRaw.Length == 0 || destRaw.Length == 0 ? "Pick both the source and destination folders." : null,
            };
        },
    };

    private static TaskRecipe PowerAction() => new()
    {
        Id = "power",
        Title = "Lock, sleep, shut down, or restart",
        Description = "Schedule a power action, for example shut down the PC every night.",
        Glyph = "\uE7E8", // PowerButton
        Fields = new[]
        {
            new WizardField { Key = "action", Label = "Action", Kind = WizardFieldKind.Choice,
                Choices = new[] { "Lock the screen", "Sleep", "Shut down", "Restart" }, Default = "Lock the screen" },
        },
        Build = f =>
        {
            var action = f.GetValueOrDefault("action", "Lock the screen");
            var (cmd, args, name) = action switch
            {
                "Sleep" => (@"C:\Windows\System32\rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0", "Sleep the PC"),
                "Shut down" => (@"C:\Windows\System32\shutdown.exe", "/s /t 0", "Shut down the PC"),
                "Restart" => (@"C:\Windows\System32\shutdown.exe", "/r /t 0", "Restart the PC"),
                _ => (@"C:\Windows\System32\rundll32.exe", "user32.dll,LockWorkStation", "Lock the screen"),
            };
            return new RecipeResult
            {
                Command = cmd,
                Arguments = args,
                Description = name + ".",
                SuggestedName = name,
                PlainSummary = action + " on the schedule you choose.",
            };
        },
    };

    private static TaskRecipe RunPowerShellScript() => new()
    {
        Id = "ps1",
        Title = "Run a PowerShell script",
        Description = "Run a .ps1 script file you already have.",
        Glyph = "\uE756", // CommandPrompt
        Fields = new[]
        {
            new WizardField { Key = "path", Label = "Script file (.ps1)", Kind = WizardFieldKind.FilePath,
                Help = "Use Browse to pick your PowerShell script." },
        },
        Build = f =>
        {
            var path = (f.GetValueOrDefault("path", "") ?? "").Trim().Replace("\"", "");
            var name = SafeFileName(path);
            return new RecipeResult
            {
                Command = PowerShell,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{path}\"",
                Description = $"Runs the PowerShell script {name}.",
                SuggestedName = name.Length > 0 ? $"Run {name}" : "Run script",
                PlainSummary = $"Run the PowerShell script {(name.Length > 0 ? name : "you selected")}.",
                Validation = path.Length == 0 ? "Pick a .ps1 script file." : null,
            };
        },
    };

    private static TaskRecipe RunCopilotPrompt() => new()
    {
        Id = "copilot",
        Title = "Send a message to GitHub Copilot",
        Description = "Start the GitHub Copilot CLI and send it a message on a schedule.",
        Glyph = "\uE99A", // Robot
        Fields = new[]
        {
            new WizardField { Key = "message", Label = "Message to send", Kind = WizardFieldKind.Multiline,
                Placeholder = "Summarize today\u2019s git changes and write them to NOTES.md",
                Help = "Copilot receives this as a single prompt each time the task runs." },
            new WizardField { Key = "dir", Label = "Work in this folder (optional)", Kind = WizardFieldKind.FolderPath,
                Help = "Copilot runs here so it can see and change files in this folder. Leave blank to use your home folder." },
            new WizardField { Key = "tools", Label = "Let Copilot run tools automatically?", Kind = WizardFieldKind.Choice,
                Choices = new[] { "Yes, let it do the work", "No, just reply" },
                Default = "Yes, let it do the work",
                Help = "\u201CYes\u201D lets Copilot edit files and run commands without prompting; needed for unattended tasks." },
            new WizardField { Key = "log", Label = "Save the reply to a file (optional)", Kind = WizardFieldKind.Text,
                Placeholder = "C:\\Users\\you\\copilot-output.txt",
                Help = "If set, everything Copilot prints is written here so you can read it later." },
        },
        Build = f =>
        {
            var msgRaw = (f.GetValueOrDefault("message", "") ?? "").Trim();
            var msg = Ps(msgRaw.Replace('"', '\''));
            var dirRaw = (f.GetValueOrDefault("dir", "") ?? "").Trim();
            var logRaw = (f.GetValueOrDefault("log", "") ?? "").Trim();
            var allow = (f.GetValueOrDefault("tools", "") ?? "").StartsWith("Yes");

            var sb = new System.Text.StringBuilder();
            if (dirRaw.Length > 0) sb.Append($"Set-Location -LiteralPath '{Ps(dirRaw)}'; ");
            sb.Append($"copilot -p '{msg}'");
            if (allow) sb.Append(" --allow-all-tools");
            sb.Append(" --no-color");
            if (logRaw.Length > 0) sb.Append($" *> '{Ps(logRaw)}'");

            return new RecipeResult
            {
                Command = PowerShell,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{sb}\"",
                WorkingDirectory = dirRaw,
                Description = "Sends a message to the GitHub Copilot CLI.",
                SuggestedName = "Copilot message",
                PlainSummary = $"Start GitHub Copilot and send: \u201C{msgRaw}\u201D" + (dirRaw.Length > 0 ? $" (in {dirRaw})" : "") + ".",
                Warning = (allow ? "Copilot runs with --allow-all-tools, so it can edit files and run commands unattended. " : "") +
                          "Requires the GitHub Copilot CLI (\u201Ccopilot\u201D) installed, on PATH, and signed in.",
                Validation = msgRaw.Length == 0 ? "Enter the message to send to Copilot." : null,
            };
        },
    };

    // ---------------------------------------------------------------- helpers

    /// <summary>Escapes a value for safe insertion into a single-quoted PowerShell string.</summary>
    private static string Ps(string? value) => (value ?? string.Empty).Replace("'", "''");

    /// <summary>Base64-encodes a PowerShell script for powershell.exe -EncodedCommand (UTF-16LE,
    /// as PowerShell requires), so free-text user input can't break command-line quoting.</summary>
    private static string EncodePs(string script) =>
        Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));

    private static int ParseInt(string? s, int dflt) => int.TryParse(s, out var v) ? v : dflt;

    private static string SafeFileName(string path)
    {
        try { return string.IsNullOrWhiteSpace(path) ? "" : System.IO.Path.GetFileName(path.TrimEnd('\\')); }
        catch { return ""; }
    }
}
