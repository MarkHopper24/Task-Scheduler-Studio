using System.IO;
using System.IO.Compression;
using System.Text;
using Tasker.Core;

namespace Tasker_App.Services;

/// <summary>Backs up and restores whole folders of tasks as a zip of Task Scheduler XML files.</summary>
public static class BackupService
{
    /// <summary>Exports every task under <paramref name="folder"/> (recursively) into a zip stream.</summary>
    public static async Task<int> BackupFolderAsync(string folder, Stream output)
    {
        var tasks = await TaskerClient.ListTasksAsync(folder, recursive: true);
        var count = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var t in tasks)
        {
            string xml;
            try { xml = await TaskerClient.ExportXmlAsync(t.Path); }
            catch { continue; }

            var entryName = MakeEntryName(t.Path);
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            await using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(xml);
            await stream.WriteAsync(bytes);
            count++;
        }
        return count;
    }

    /// <summary>Restores tasks from a backup zip into <paramref name="targetFolder"/>, preserving
    /// each task's relative subfolder layout from the archive.</summary>
    public static async Task<(int restored, int failed)> RestoreAsync(Stream input, string targetFolder)
    {
        var restored = 0;
        var failed = 0;
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;

            string xml;
            await using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                xml = await reader.ReadToEndAsync();

            var relDir = Path.GetDirectoryName(entry.FullName)?.Replace('/', '\\') ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(entry.Name);
            var folder = CombineFolder(targetFolder, relDir);

            var result = await TaskerClient.ImportXmlAsync(folder, name, xml);
            if (result.Success) restored++; else failed++;
        }
        return (restored, failed);
    }

    private static string MakeEntryName(string taskPath)
    {
        // "\Folder\Sub\Task" -> "Folder/Sub/Task.xml"
        var trimmed = taskPath.TrimStart('\\').Replace('\\', '/');
        return trimmed + ".xml";
    }

    private static string CombineFolder(string target, string relDir)
    {
        target = string.IsNullOrWhiteSpace(target) ? "\\" : target.TrimEnd('\\');
        if (target.Length == 0) target = "\\";
        if (string.IsNullOrEmpty(relDir)) return target.Length == 0 ? "\\" : target;
        var combined = (target == "\\" ? "" : target) + "\\" + relDir.Trim('\\');
        return combined.StartsWith('\\') ? combined : "\\" + combined;
    }
}
