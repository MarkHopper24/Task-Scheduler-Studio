using System.Diagnostics;

namespace Tasker_App.Services;

/// <summary>
/// Interactive GitHub authentication via the GitHub CLI (<c>gh</c>). Reuses an existing
/// <c>gh auth</c> session when present, otherwise launches <c>gh auth login --web</c> so the user
/// can complete the device/web flow. The resulting token is used by the embedded Copilot CLI.
/// </summary>
public static class GitHubAuth
{
    public static async Task<bool> IsAvailableAsync()
    {
        var (code, _, _) = await RunCaptureAsync("--version");
        return code == 0;
    }

    /// <summary>Returns the token from the current <c>gh</c> session, or null if not signed in.</summary>
    public static async Task<string?> TryGetTokenAsync()
    {
        var (code, output, _) = await RunCaptureAsync("auth token");
        if (code != 0) return null;
        var token = output.Trim();
        return string.IsNullOrEmpty(token) ? null : token;
    }

    public static async Task<string?> GetUserLoginAsync()
    {
        var (code, output, _) = await RunCaptureAsync("api user --jq .login");
        return code == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
    }

    /// <summary>Launches the interactive <c>gh auth login</c> web flow in its own console window and
    /// waits for it to finish. Returns true on success.</summary>
    public static async Task<bool> LoginInteractiveAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = "auth login --web --hostname github.com --git-protocol https",
                UseShellExecute = true, // visible console so the user sees the one-time code and prompts
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    public static async Task LogoutAsync()
    {
        try { await RunCaptureAsync("auth logout --hostname github.com"); } catch { }
    }

    private static async Task<(int code, string stdout, string stderr)> RunCaptureAsync(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return (-1, string.Empty, "gh not found");
            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await outTask, await errTask);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}
