using System.IO;
using System.Text;

namespace Tasker_App.Services;

/// <summary>
/// Writes a chosen library script to a real <c>.ps1</c> file under
/// <c>%LOCALAPPDATA%\WindowsTasker\scripts</c> and builds the task action that runs it via
/// <c>powershell.exe -File</c>. Keeping scripts on disk (rather than inline) lets the user edit them
/// later, handles arbitrarily long scripts, and keeps the Task Scheduler action arguments clean.
/// </summary>
public static class ScriptMaterializer
{
    public const string PowerShellExe = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";

    public static string ScriptsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsTasker", "scripts");

    /// <summary>Writes <paramref name="text"/> to a uniquely-named .ps1 (based on
    /// <paramref name="suggestedName"/>) in <see cref="ScriptsDir"/> and returns its full path. The
    /// file is UTF-8 with a BOM so Windows PowerShell 5.1 reads non-ASCII characters correctly.</summary>
    public static string Write(string suggestedName, string text)
    {
        Directory.CreateDirectory(ScriptsDir);
        var baseName = Sanitize(suggestedName);
        var path = Path.Combine(ScriptsDir, baseName + ".ps1");
        var i = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(ScriptsDir, $"{baseName}-{i}.ps1");
            i++;
        }
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    /// <summary>Builds the (program, arguments) for a task action that runs the given script.</summary>
    public static (string command, string arguments) BuildAction(string ps1Path, bool background)
    {
        var args = "-NoProfile -ExecutionPolicy Bypass"
                   + (background ? " -WindowStyle Hidden" : string.Empty)
                   + $" -File \"{ps1Path}\"";
        return (PowerShellExe, args);
    }

    private static string Sanitize(string name)
    {
        name = (name ?? string.Empty).Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '-');
        name = name.Replace(' ', '-');
        if (name.Length > 60) name = name[..60];
        return string.IsNullOrWhiteSpace(name) ? "script" : name;
    }
}
