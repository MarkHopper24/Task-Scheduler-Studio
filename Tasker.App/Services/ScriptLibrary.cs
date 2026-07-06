using System.Text;

namespace Tasker_App.Services;

/// <summary>Broad grouping for a script in the library, used for filtering and category chips.</summary>
public enum ScriptCategory { Cleanup, Backup, Maintenance, Monitoring, System }

/// <summary>
/// A reusable PowerShell script template. Built-in templates supply a <see cref="Builder"/> that
/// renders the final script from the user's friendly <see cref="Fields"/> (which prefill a commented
/// "Settings" block at the top for easy tweaks). User-saved scripts instead carry raw <see cref="Body"/>
/// text. Either way the chosen script is materialized to a .ps1 and run via <c>powershell -File</c>.
/// </summary>
public sealed class ScriptTemplate
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ScriptCategory Category { get; init; } = ScriptCategory.System;
    public string Glyph { get; init; } = "\uE756"; // CommandPrompt
    /// <summary>True if the script needs elevation; the create flow suggests "Run with highest privileges".</summary>
    public bool RequiresAdmin { get; init; }
    /// <summary>True to run hidden in the background (adds <c>-WindowStyle Hidden</c> to the action).</summary>
    public bool Background { get; init; } = true;

    /// <summary>Friendly inputs for the common tweaks (reuses the wizard field model). Built-ins only.</summary>
    public IReadOnlyList<WizardField> Fields { get; init; } = Array.Empty<WizardField>();

    /// <summary>Built-in renderer: turns field values into the full script text.</summary>
    public Func<IReadOnlyDictionary<string, string>, string>? Builder { get; init; }

    /// <summary>Raw script text for a user-saved script (no <see cref="Builder"/>).</summary>
    public string? Body { get; init; }

    public bool IsBuiltIn => Builder is not null;

    // Accessible name for the script list row container (avoids announcing the class name).
    public override string ToString() => Title;

    public string CategoryName => Category switch
    {
        ScriptCategory.Cleanup => "Cleanup",
        ScriptCategory.Backup => "Backup",
        ScriptCategory.Maintenance => "Maintenance",
        ScriptCategory.Monitoring => "Monitoring",
        ScriptCategory.System => "System",
        _ => Category.ToString(),
    };

    /// <summary>Renders the script from explicit field values (built-in) or returns the raw body (user).</summary>
    public string BuildScript(IReadOnlyDictionary<string, string> values)
        => Builder is not null ? Builder(values) : (Body ?? string.Empty);

    /// <summary>Renders the script using every field's default value (the initial preview).</summary>
    public string DefaultScript()
        => BuildScript(Fields.ToDictionary(f => f.Key, f => f.Default));
}

/// <summary>
/// Curated catalog of common, safe Windows/PowerShell automation scripts offered as ready-made
/// templates. Deletions of user files go to the Recycle Bin; only transient caches are hard-deleted
/// (scoped, with <c>-ErrorAction SilentlyContinue</c>); notifications use Windows 11 toasts without
/// registering an app; admin-only scripts are flagged. Every script runs <c>-NoProfile
/// -ExecutionPolicy Bypass</c> and is shown for review before a task is created.
/// </summary>
public static class ScriptLibrary
{
    public static IReadOnlyList<ScriptTemplate> BuiltIn { get; } = new[]
    {
        CleanTemp(), EmptyRecycleBin(), TidyOldFiles(), OrganizeByType(), ClearUpdateCache(),
        ZipBackup(), RobocopyMirror(), ExportInstalledApps(),
        RestorePoint(), OptimizeVolume(), RestartService(), RestartExplorer(), UpdateScan(),
        LowDiskAlert(), UptimeHeartbeat(), ExportFailedSignins(), PingMonitor(),
        ReminderToast(), BatteryReport(), ResyncClock(),
    };

    public static ScriptTemplate? ById(string id) => BuiltIn.FirstOrDefault(s => s.Id == id);

    // ---------------------------------------------------------------- helpers

    private static string Downloads => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    private static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private static int NumOr(IReadOnlyDictionary<string, string> v, string key, int dflt)
        => int.TryParse(v.GetValueOrDefault(key), out var n) ? n : dflt;

    private static string TextOr(IReadOnlyDictionary<string, string> v, string key, string dflt)
    {
        var s = v.GetValueOrDefault(key);
        return string.IsNullOrWhiteSpace(s) ? dflt : s.Trim();
    }

    /// <summary>Renders a value as a PowerShell double-quoted string (allows $env expansion, escapes quotes).</summary>
    private static string Dq(string s) => "\"" + s.Replace("`", "``").Replace("\"", "`\"") + "\"";

    /// <summary>Builds the commented, editable "Settings" block that opens each built-in script.</summary>
    private static string Settings(params string[] assignments)
    {
        var sb = new StringBuilder();
        sb.Append("# ===== Settings (edit these) =====\r\n");
        foreach (var a in assignments) sb.Append(a).Append("\r\n");
        sb.Append("# =================================\r\n\r\n");
        return sb.ToString();
    }

    private static string Header(string title) => $"# {title}\r\n\r\n";

    // Shared toast helper injected into notification scripts. Raises a Windows 11 toast without
    // installing a module or registering an app by borrowing an already-registered AUMID: it tries
    // Windows Task Studio's packaged AUMID first (so the toast is attributed to the app), then falls
    // back to File Explorer's AUMID.
    private const string ToastFunction =
"""
function Show-Toast {
    param([string]$Title, [string]$Message)
    [void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
    [void][Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
    $xmlText = "<toast><visual><binding template=`"ToastGeneric`"><text>$Title</text><text>$Message</text></binding></visual></toast>"
    $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
    $xml.LoadXml($xmlText)
    $toast = New-Object Windows.UI.Notifications.ToastNotification $xml
    foreach ($aumid in @("53383MarkHopper.WindowsTasker_25krmwkbgncsa!App", "Microsoft.Windows.Explorer")) {
        try { [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($aumid).Show($toast); return } catch { }
    }
}

""";

    // ---------------------------------------------------------------- Cleanup

    private static ScriptTemplate CleanTemp() => new()
    {
        Id = "clean-temp",
        Title = "Clean temporary files",
        Description = "Delete files older than a chosen age from your temp folders (safe: only transient temp files).",
        Category = ScriptCategory.Cleanup,
        Glyph = "\uE74D",
        Fields = new[]
        {
            new WizardField { Key = "days", Label = "Delete temp files older than (days)", Kind = WizardFieldKind.Number, Default = "7", Min = 0, Max = 3650 },
        },
        Builder = v => Header("Clean temporary files") + Settings($"$Days = {NumOr(v, "days", 7)}") +
"""
$targets = @("$env:TEMP", "$env:SystemRoot\Temp")
$cutoff = (Get-Date).AddDays(-$Days)
foreach ($t in $targets) {
    if (Test-Path $t) {
        Get-ChildItem -Path $t -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { -not $_.PSIsContainer -and $_.LastWriteTime -lt $cutoff } |
            Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
    }
}
""",
    };

    private static ScriptTemplate EmptyRecycleBin() => new()
    {
        Id = "empty-recyclebin",
        Title = "Empty the Recycle Bin",
        Description = "Permanently clear the Recycle Bin for the current user.",
        Category = ScriptCategory.Cleanup,
        Glyph = "\uE74D",
        Builder = _ => Header("Empty the Recycle Bin") +
"""
Clear-RecycleBin -Force -ErrorAction SilentlyContinue
""",
    };

    private static ScriptTemplate TidyOldFiles() => new()
    {
        Id = "tidy-old-files",
        Title = "Tidy old files (to Recycle Bin)",
        Description = "Send files older than a chosen age in a folder to the Recycle Bin, so nothing is lost permanently.",
        Category = ScriptCategory.Cleanup,
        Glyph = "\uE74D",
        Fields = new[]
        {
            new WizardField { Key = "folder", Label = "Folder to tidy", Kind = WizardFieldKind.FolderPath, Default = Downloads },
            new WizardField { Key = "days", Label = "Older than (days)", Kind = WizardFieldKind.Number, Default = "30", Min = 1, Max = 3650 },
        },
        Builder = v => Header("Tidy old files (to Recycle Bin)") + Settings(
                $"$Folder = {Dq(TextOr(v, "folder", Downloads))}",
                $"$Days = {NumOr(v, "days", 30)}") +
"""
Add-Type -AssemblyName Microsoft.VisualBasic
$cutoff = (Get-Date).AddDays(-$Days)
Get-ChildItem -LiteralPath $Folder -File -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt $cutoff } |
    ForEach-Object { [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($_.FullName, 'OnlyErrorDialogs', 'SendToRecycleBin') }
""",
    };

    private static ScriptTemplate OrganizeByType() => new()
    {
        Id = "organize-by-type",
        Title = "Organize a folder by file type",
        Description = "Move files into Images, Documents, Video, Audio, Archives, Installers and Other subfolders.",
        Category = ScriptCategory.Cleanup,
        Glyph = "\uE8B7",
        Fields = new[]
        {
            new WizardField { Key = "folder", Label = "Folder to organize", Kind = WizardFieldKind.FolderPath, Default = Downloads },
        },
        Builder = v => Header("Organize a folder by file type") + Settings(
                $"$Folder = {Dq(TextOr(v, "folder", Downloads))}") +
"""
$map = [ordered]@{
    Images     = @(".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".svg")
    Documents  = @(".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".rtf", ".md")
    Video      = @(".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm")
    Audio      = @(".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg")
    Archives   = @(".zip", ".rar", ".7z", ".tar", ".gz", ".iso")
    Installers = @(".exe", ".msi")
}
Get-ChildItem -LiteralPath $Folder -File -ErrorAction SilentlyContinue | ForEach-Object {
    $ext = $_.Extension.ToLower()
    $cat = "Other"
    foreach ($entry in $map.GetEnumerator()) { if ($entry.Value -contains $ext) { $cat = $entry.Key; break } }
    $dest = Join-Path $Folder $cat
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Move-Item -LiteralPath $_.FullName -Destination $dest -Force -ErrorAction SilentlyContinue
}
""",
    };

    private static ScriptTemplate ClearUpdateCache() => new()
    {
        Id = "clear-update-cache",
        Title = "Clear Windows Update cache",
        Description = "Stop the update services, delete the downloaded update cache, and restart them. Frees space and fixes stuck updates.",
        Category = ScriptCategory.Cleanup,
        Glyph = "\uE74D",
        RequiresAdmin = true,
        Builder = _ => Header("Clear Windows Update cache") +
"""
$services = @("wuauserv", "bits")
$services | ForEach-Object { Stop-Service -Name $_ -Force -ErrorAction SilentlyContinue }
Remove-Item -Path "$env:SystemRoot\SoftwareDistribution\Download\*" -Recurse -Force -ErrorAction SilentlyContinue
$services | ForEach-Object { Start-Service -Name $_ -ErrorAction SilentlyContinue }
""",
    };

    // ---------------------------------------------------------------- Backup

    private static ScriptTemplate ZipBackup() => new()
    {
        Id = "zip-backup",
        Title = "Back up a folder to a dated ZIP",
        Description = "Compress a folder into a timestamped .zip, keeping only the most recent backups.",
        Category = ScriptCategory.Backup,
        Glyph = "\uE8C8",
        Fields = new[]
        {
            new WizardField { Key = "source", Label = "Folder to back up", Kind = WizardFieldKind.FolderPath, Default = Documents },
            new WizardField { Key = "dest", Label = "Backup location", Kind = WizardFieldKind.FolderPath, Default = System.IO.Path.Combine(Documents, "Backups") },
            new WizardField { Key = "keep", Label = "Keep this many recent backups", Kind = WizardFieldKind.Number, Default = "7", Min = 1, Max = 365 },
        },
        Builder = v => Header("Back up a folder to a dated ZIP") + Settings(
                $"$Source = {Dq(TextOr(v, "source", Documents))}",
                $"$Dest = {Dq(TextOr(v, "dest", System.IO.Path.Combine(Documents, "Backups")))}",
                $"$Keep = {NumOr(v, "keep", 7)}") +
"""
if (-not (Test-Path $Source)) { throw "Source folder not found: $Source" }
New-Item -ItemType Directory -Force -Path $Dest | Out-Null
$leaf = Split-Path $Source -Leaf
$stamp = Get-Date -Format "yyyy-MM-dd_HHmm"
$zip = Join-Path $Dest ("{0}_{1}.zip" -f $leaf, $stamp)
Compress-Archive -Path (Join-Path $Source '*') -DestinationPath $zip -Force
Get-ChildItem -LiteralPath $Dest -Filter ("{0}_*.zip" -f $leaf) -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -Skip $Keep |
    Remove-Item -Force -ErrorAction SilentlyContinue
""",
    };

    private static ScriptTemplate RobocopyMirror() => new()
    {
        Id = "robocopy-mirror",
        Title = "Mirror a folder (Robocopy)",
        Description = "Keep a backup folder identical to a source using Robocopy /MIR, with a log. Files removed from the source are removed from the mirror.",
        Category = ScriptCategory.Backup,
        Glyph = "\uE8C8",
        Fields = new[]
        {
            new WizardField { Key = "source", Label = "Source folder", Kind = WizardFieldKind.FolderPath, Default = Documents },
            new WizardField { Key = "dest", Label = "Mirror (destination) folder", Kind = WizardFieldKind.FolderPath, Default = System.IO.Path.Combine(Documents, "Mirror") },
        },
        Builder = v => Header("Mirror a folder (Robocopy)") + Settings(
                $"$Source = {Dq(TextOr(v, "source", Documents))}",
                $"$Dest = {Dq(TextOr(v, "dest", System.IO.Path.Combine(Documents, "Mirror")))}") +
"""
New-Item -ItemType Directory -Force -Path $Dest | Out-Null
$log = Join-Path $Dest "robocopy.log"
robocopy $Source $Dest /MIR /R:2 /W:5 /NP /LOG+:$log
# Robocopy exit codes 0-7 indicate success; 8 or higher indicates an error.
if ($LASTEXITCODE -ge 8) { throw "Robocopy reported errors (exit code $LASTEXITCODE). See $log" }
""",
    };

    private static ScriptTemplate ExportInstalledApps() => new()
    {
        Id = "export-installed-apps",
        Title = "Export installed apps list",
        Description = "Save a dated list of installed applications (via winget, or Get-Package as a fallback).",
        Category = ScriptCategory.Backup,
        Glyph = "\uE71D",
        Fields = new[]
        {
            new WizardField { Key = "folder", Label = "Save the list in", Kind = WizardFieldKind.FolderPath, Default = Documents },
        },
        Builder = v => Header("Export installed apps list") + Settings(
                $"$OutFolder = {Dq(TextOr(v, "folder", Documents))}") +
"""
New-Item -ItemType Directory -Force -Path $OutFolder | Out-Null
$stamp = Get-Date -Format "yyyy-MM-dd"
$out = Join-Path $OutFolder ("installed-apps_{0}.txt" -f $stamp)
if (Get-Command winget -ErrorAction SilentlyContinue) {
    winget list --disable-interactivity | Out-File -FilePath $out -Encoding UTF8
} else {
    Get-Package | Sort-Object Name | Format-Table -AutoSize | Out-File -FilePath $out -Encoding UTF8
}
""",
    };

    // ---------------------------------------------------------------- Maintenance

    private static ScriptTemplate RestorePoint() => new()
    {
        Id = "restore-point",
        Title = "Create a System Restore Point",
        Description = "Create a restore point so you can roll Windows back if something goes wrong. System Protection must be enabled.",
        Category = ScriptCategory.Maintenance,
        Glyph = "\uE777",
        RequiresAdmin = true,
        Fields = new[]
        {
            new WizardField { Key = "name", Label = "Restore point name", Kind = WizardFieldKind.Text, Default = "Scheduled restore point" },
        },
        Builder = v => Header("Create a System Restore Point") + Settings(
                $"$Name = {Dq(TextOr(v, "name", "Scheduled restore point"))}") +
"""
Checkpoint-Computer -Description $Name -RestorePointType "MODIFY_SETTINGS"
""",
    };

    private static ScriptTemplate OptimizeVolume() => new()
    {
        Id = "optimize-volume",
        Title = "Optimize a drive",
        Description = "Defragment a hard drive or re-trim an SSD. Windows picks the right operation for the drive type.",
        Category = ScriptCategory.Maintenance,
        Glyph = "\uEB7E",
        RequiresAdmin = true,
        Fields = new[]
        {
            new WizardField { Key = "drive", Label = "Drive letter", Kind = WizardFieldKind.Text, Default = "C" },
        },
        Builder = v => Header("Optimize a drive") + Settings(
                $"$Drive = {Dq(TextOr(v, "drive", "C"))}") +
"""
Optimize-Volume -DriveLetter $Drive -Verbose
""",
    };

    private static ScriptTemplate RestartService() => new()
    {
        Id = "restart-service",
        Title = "Restart a Windows service",
        Description = "Restart a service by name (for example Spooler, Audiosrv, or Dnscache).",
        Category = ScriptCategory.Maintenance,
        Glyph = "\uE9F5",
        RequiresAdmin = true,
        Fields = new[]
        {
            new WizardField { Key = "service", Label = "Service name", Kind = WizardFieldKind.Text, Default = "Spooler", Help = "Use the short service name shown in services.msc (right-click a service, Properties)." },
        },
        Builder = v => Header("Restart a Windows service") + Settings(
                $"$Service = {Dq(TextOr(v, "service", "Spooler"))}") +
"""
Restart-Service -Name $Service -Force
""",
    };

    private static ScriptTemplate RestartExplorer() => new()
    {
        Id = "restart-explorer",
        Title = "Restart Windows Explorer",
        Description = "Restart the Explorer shell (taskbar and desktop). Handy when the taskbar becomes unresponsive.",
        Category = ScriptCategory.Maintenance,
        Glyph = "\uE9F5",
        Builder = _ => Header("Restart Windows Explorer") +
"""
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
# Explorer relaunches itself automatically; start it explicitly if it does not.
Start-Sleep -Seconds 2
if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
""",
    };

    private static ScriptTemplate UpdateScan() => new()
    {
        Id = "update-scan",
        Title = "Check for Windows updates",
        Description = "Trigger a Windows Update scan in the background so updates download and install on your schedule.",
        Category = ScriptCategory.Maintenance,
        Glyph = "\uE895",
        RequiresAdmin = true,
        Builder = _ => Header("Check for Windows updates") +
"""
Start-Process -FilePath "$env:SystemRoot\System32\UsoClient.exe" -ArgumentList "StartScan" -WindowStyle Hidden
""",
    };

    // ---------------------------------------------------------------- Monitoring

    private static ScriptTemplate LowDiskAlert() => new()
    {
        Id = "low-disk-alert",
        Title = "Low disk space alert",
        Description = "Show a Windows notification and log a line when free space on a drive drops below a threshold.",
        Category = ScriptCategory.Monitoring,
        Glyph = "\uE7BA",
        Fields = new[]
        {
            new WizardField { Key = "drive", Label = "Drive letter", Kind = WizardFieldKind.Text, Default = "C" },
            new WizardField { Key = "gb", Label = "Warn when free space is below (GB)", Kind = WizardFieldKind.Number, Default = "10", Min = 1, Max = 100000 },
            new WizardField { Key = "log", Label = "Log file", Kind = WizardFieldKind.Text, Default = System.IO.Path.Combine(Documents, "disk-space.csv") },
        },
        Builder = v => Header("Low disk space alert") + Settings(
                $"$Drive = {Dq(TextOr(v, "drive", "C"))}",
                $"$ThresholdGB = {NumOr(v, "gb", 10)}",
                $"$LogPath = {Dq(TextOr(v, "log", System.IO.Path.Combine(Documents, "disk-space.csv")))}") +
                "\r\n" + ToastFunction +
"""
$vol = Get-PSDrive -Name $Drive -ErrorAction SilentlyContinue
if ($vol) {
    $freeGB = [math]::Round($vol.Free / 1GB, 1)
    if ($freeGB -lt $ThresholdGB) {
        $msg = "Drive $Drive has $freeGB GB free (below $ThresholdGB GB)."
        Show-Toast "Low disk space" $msg
        if ($LogPath) { "$(Get-Date -Format s),$Drive,$freeGB" | Add-Content -Path $LogPath }
    }
}
""",
    };

    private static ScriptTemplate UptimeHeartbeat() => new()
    {
        Id = "uptime-heartbeat",
        Title = "Log system uptime",
        Description = "Append the current time and how long the PC has been running to a CSV log each time it runs.",
        Category = ScriptCategory.Monitoring,
        Glyph = "\uE823",
        Fields = new[]
        {
            new WizardField { Key = "log", Label = "Log file (CSV)", Kind = WizardFieldKind.Text, Default = System.IO.Path.Combine(Documents, "uptime.csv") },
        },
        Builder = v => Header("Log system uptime") + Settings(
                $"$LogPath = {Dq(TextOr(v, "log", System.IO.Path.Combine(Documents, "uptime.csv")))}") +
"""
$os = Get-CimInstance Win32_OperatingSystem
$uptime = (Get-Date) - $os.LastBootUpTime
if (-not (Test-Path $LogPath)) { "Timestamp,Uptime" | Set-Content -Path $LogPath }
("{0},{1:dd\.hh\:mm\:ss}" -f (Get-Date -Format s), $uptime) | Add-Content -Path $LogPath
""",
    };

    private static ScriptTemplate ExportFailedSignins() => new()
    {
        Id = "export-failed-signins",
        Title = "Export failed sign-ins",
        Description = "Save failed sign-in attempts (Security event 4625) from the last few days to a CSV for review.",
        Category = ScriptCategory.Monitoring,
        Glyph = "\uE72E",
        RequiresAdmin = true,
        Fields = new[]
        {
            new WizardField { Key = "days", Label = "Look back (days)", Kind = WizardFieldKind.Number, Default = "1", Min = 1, Max = 90 },
            new WizardField { Key = "folder", Label = "Save the report in", Kind = WizardFieldKind.FolderPath, Default = Documents },
        },
        Builder = v => Header("Export failed sign-ins") + Settings(
                $"$Days = {NumOr(v, "days", 1)}",
                $"$OutFolder = {Dq(TextOr(v, "folder", Documents))}") +
"""
New-Item -ItemType Directory -Force -Path $OutFolder | Out-Null
$start = (Get-Date).AddDays(-$Days)
$stamp = Get-Date -Format "yyyy-MM-dd"
$out = Join-Path $OutFolder ("failed-signins_{0}.csv" -f $stamp)
$events = Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = 4625; StartTime = $start } -ErrorAction SilentlyContinue
if ($events) {
    $events | Select-Object TimeCreated,
        @{ N = 'Account'; E = { $_.Properties[5].Value } },
        @{ N = 'Workstation'; E = { $_.Properties[13].Value } },
        @{ N = 'SourceIp'; E = { $_.Properties[19].Value } } |
        Export-Csv -Path $out -NoTypeInformation
}
""",
    };

    private static ScriptTemplate PingMonitor() => new()
    {
        Id = "ping-monitor",
        Title = "Ping a host and log connectivity",
        Description = "Ping a server or website and append UP or DOWN with a timestamp to a CSV log.",
        Category = ScriptCategory.Monitoring,
        Glyph = "\uE774",
        Fields = new[]
        {
            new WizardField { Key = "target", Label = "Host or IP to ping", Kind = WizardFieldKind.Text, Default = "8.8.8.8" },
            new WizardField { Key = "log", Label = "Log file (CSV)", Kind = WizardFieldKind.Text, Default = System.IO.Path.Combine(Documents, "ping.csv") },
        },
        Builder = v => Header("Ping a host and log connectivity") + Settings(
                $"$Target = {Dq(TextOr(v, "target", "8.8.8.8"))}",
                $"$LogPath = {Dq(TextOr(v, "log", System.IO.Path.Combine(Documents, "ping.csv")))}") +
"""
$ok = Test-Connection -ComputerName $Target -Count 1 -Quiet -ErrorAction SilentlyContinue
$status = if ($ok) { "UP" } else { "DOWN" }
if (-not (Test-Path $LogPath)) { "Timestamp,Target,Status" | Set-Content -Path $LogPath }
"$(Get-Date -Format s),$Target,$status" | Add-Content -Path $LogPath
""",
    };

    // ---------------------------------------------------------------- System

    private static ScriptTemplate ReminderToast() => new()
    {
        Id = "reminder-toast",
        Title = "Show a reminder notification",
        Description = "Pop up a Windows 11 toast notification with your message. Great for recurring reminders.",
        Category = ScriptCategory.System,
        Glyph = "\uE7E7",
        Fields = new[]
        {
            new WizardField { Key = "title", Label = "Title", Kind = WizardFieldKind.Text, Default = "Reminder" },
            new WizardField { Key = "message", Label = "Message", Kind = WizardFieldKind.Multiline, Default = "Time to take a break and stretch!" },
        },
        Builder = v => Header("Show a reminder notification") + Settings(
                $"$Title = {Dq(TextOr(v, "title", "Reminder"))}",
                $"$Message = {Dq(TextOr(v, "message", "Time to take a break and stretch!"))}") +
                "\r\n" + ToastFunction +
"""
Show-Toast $Title $Message
""",
    };

    private static ScriptTemplate BatteryReport() => new()
    {
        Id = "battery-report",
        Title = "Generate a battery health report",
        Description = "Save a detailed HTML battery report (capacity history, usage, and estimated life) for a laptop.",
        Category = ScriptCategory.System,
        Glyph = "\uE83F",
        Fields = new[]
        {
            new WizardField { Key = "folder", Label = "Save the report in", Kind = WizardFieldKind.FolderPath, Default = Documents },
        },
        Builder = v => Header("Generate a battery health report") + Settings(
                $"$OutFolder = {Dq(TextOr(v, "folder", Documents))}") +
"""
New-Item -ItemType Directory -Force -Path $OutFolder | Out-Null
$stamp = Get-Date -Format "yyyy-MM-dd"
$out = Join-Path $OutFolder ("battery-report_{0}.html" -f $stamp)
powercfg /batteryreport /output "$out"
""",
    };

    private static ScriptTemplate ResyncClock() => new()
    {
        Id = "resync-clock",
        Title = "Sync the system clock",
        Description = "Force Windows to resynchronize the system clock with its time server.",
        Category = ScriptCategory.System,
        Glyph = "\uE823",
        RequiresAdmin = true,
        Builder = _ => Header("Sync the system clock") +
"""
Start-Service w32time -ErrorAction SilentlyContinue
w32tm /resync
""",
    };
}
