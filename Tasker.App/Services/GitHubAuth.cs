using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Tasker_App.Services;

/// <summary>A single GitHub CLI account for a host, as reported by <c>gh auth status</c>.</summary>
public sealed record GhAccount(string Login, bool IsActive);

/// <summary>
/// Interactive GitHub authentication via the GitHub CLI (<c>gh</c>). Reuses an existing
/// <c>gh auth</c> session when present, otherwise launches <c>gh auth login --web</c> so the user
/// can complete the device/web flow. The resulting token is used by the embedded Copilot CLI.
///
/// The GitHub CLI supports multiple signed-in accounts per host (one "active" at a time), which
/// this class fully accounts for: every command that acts on "the" account (logout, status) is
/// always given an explicit <c>--user</c> so it can never fall back to gh's own interactive
/// account-picker prompt — that prompt has no terminal to render against when launched from this
/// app (stdio is redirected, not a real console), so previously it would silently fail instead of
/// asking, which is the root cause of reported "logout doesn't work with multiple accounts" issues.
/// </summary>
public static class GitHubAuth
{
    private const string Hostname = "github.com";

    public static async Task<bool> IsAvailableAsync()
    {
        var (code, _, _) = await RunCaptureAsync("--version");
        return code == 0;
    }

    /// <summary>Returns the token for the currently-active <c>gh</c> account, or null if not signed in.</summary>
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

    /// <summary>All accounts <c>gh</c> currently has signed in for github.com, with the active one
    /// flagged. Empty if <c>gh</c> isn't installed or nothing is signed in. Purely a status read —
    /// safe to call any time, never changes sign-in state.</summary>
    public static async Task<IReadOnlyList<GhAccount>> ListAccountsAsync()
    {
        var (code, output, stderr) = await RunCaptureAsync($"auth status --hostname {Hostname}");
        // "not logged in" and similar are expected, non-error outcomes here, not a real failure.
        if (code != 0 && string.IsNullOrWhiteSpace(output)) output = stderr;
        return ParseAccounts(output);
    }

    /// <summary>Parses the account/active-flag pairs out of <c>gh auth status</c>'s text output,
    /// e.g. "✓ Logged in to github.com account someuser (keyring)" followed by
    /// "- Active account: true/false".</summary>
    internal static IReadOnlyList<GhAccount> ParseAccounts(string statusText)
    {
        var accounts = new List<GhAccount>();
        string? pendingLogin = null;
        foreach (var rawLine in statusText.Split('\n'))
        {
            var line = rawLine.Trim();
            var loginMatch = Regex.Match(line, @"Logged in to \S+ account (\S+)");
            if (loginMatch.Success)
            {
                pendingLogin = loginMatch.Groups[1].Value;
                continue;
            }
            if (pendingLogin is null) continue;

            var activeMatch = Regex.Match(line, @"Active account:\s*(true|false)", RegexOptions.IgnoreCase);
            if (!activeMatch.Success) continue;

            accounts.Add(new GhAccount(pendingLogin, string.Equals(activeMatch.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase)));
            pendingLogin = null;
        }
        return accounts;
    }

    /// <summary>Switches the active github.com account <c>gh</c> (and therefore this app) uses.
    /// Always passes an explicit user so it can never hit gh's interactive picker.</summary>
    public static async Task<bool> SwitchAccountAsync(string login)
    {
        var (code, _, _) = await RunCaptureAsync($"auth switch --hostname {Hostname} --user \"{login}\"");
        return code == 0;
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

    /// <summary>Logs out one specific account. Always requires an explicit login (never falls back
    /// to gh's interactive "which account?" picker, which can't render without a real console and
    /// would otherwise make this silently do nothing in a multi-account setup).</summary>
    public static async Task<bool> LogoutAsync(string login)
    {
        var (code, _, _) = await RunCaptureAsync($"auth logout --hostname {Hostname} --user \"{login}\"");
        return code == 0;
    }

    /// <summary>Signs out of whichever github.com account is currently active, resolving it first so
    /// the logout is always unambiguous. Returns true if there was nothing to log out (already
    /// signed out) or the logout succeeded; false only on a genuine failure.</summary>
    public static async Task<bool> LogoutActiveAccountAsync()
    {
        var accounts = await ListAccountsAsync();
        var active = accounts.FirstOrDefault(a => a.IsActive);
        if (active is null) return true; // nothing signed in for this host
        return await LogoutAsync(active.Login);
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
                // Redirected (not inherited from a real console) so that if gh ever falls back to
                // an interactive prompt despite our explicit --user/--hostname args, it fails fast
                // instead of hanging indefinitely waiting for input that can never arrive.
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return (-1, string.Empty, "gh not found");
            process.StandardInput.Close();
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

