using System.Diagnostics;
using System.Security.Principal;

namespace Tasker_App.Helpers;

/// <summary>Elevation detection and self-relaunch with admin rights (for creating highest-privileges
/// tasks and editing protected system tasks).</summary>
public static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    /// <summary>Launches a new elevated instance via the UAC "runas" verb. Returns false if the user
    /// declined the prompt or relaunch failed.</summary>
    public static bool RelaunchAsAdmin()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path)) return false;
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "runas",
            });
            return true;
        }
        catch { return false; }
    }
}
