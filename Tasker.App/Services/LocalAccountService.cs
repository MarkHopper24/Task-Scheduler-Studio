using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Tasker_App.Services;

public sealed record LocalAccount(string Name, string QualifiedName, bool IsServiceAccount = false);

public static class LocalAccountService
{
    private const int ErrorMoreData = 234;
    private const int FilterNormalAccount = 0x0002;
    private const uint UserAccountDisabled = 0x0002;
    private const int MaxPreferredLength = -1;

    public static IReadOnlyList<LocalAccount> GetSelectableAccounts(bool includeServiceAccounts)
    {
        var accounts = new List<LocalAccount>();
        var resumeHandle = 0;
        int status;

        do
        {
            status = NetUserEnum(
                null,
                1,
                FilterNormalAccount,
                out var buffer,
                MaxPreferredLength,
                out var entriesRead,
                out _,
                ref resumeHandle);

            if (status != 0 && status != ErrorMoreData)
                throw new Win32Exception(status, "Windows could not enumerate local user accounts.");

            try
            {
                var itemSize = Marshal.SizeOf<UserInfo1>();
                for (var index = 0; index < entriesRead; index++)
                {
                    var item = Marshal.PtrToStructure<UserInfo1>(IntPtr.Add(buffer, index * itemSize));
                    var name = Marshal.PtrToStringUni(item.Name);
                    if (string.IsNullOrWhiteSpace(name) || (item.Flags & UserAccountDisabled) != 0)
                        continue;

                    accounts.Add(new LocalAccount(name, $@"{Environment.MachineName}\{name}"));
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    _ = NetApiBufferFree(buffer);
            }
        }
        while (status == ErrorMoreData);

        var localAccounts = accounts
            .OrderBy(account => account.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (includeServiceAccounts)
        {
            localAccounts.AddRange([
                new LocalAccount("SYSTEM", "SYSTEM", IsServiceAccount: true),
                new LocalAccount("LOCAL SERVICE", "LOCAL SERVICE", IsServiceAccount: true),
                new LocalAccount("NETWORK SERVICE", "NETWORK SERVICE", IsServiceAccount: true),
            ]);
        }

        return localAccounts;
    }

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserEnum(
        string? serverName,
        int level,
        int filter,
        out IntPtr buffer,
        int preferredMaximumLength,
        out int entriesRead,
        out int totalEntries,
        ref int resumeHandle);

    [DllImport("Netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct UserInfo1
    {
        public IntPtr Name;
        public IntPtr Password;
        public uint PasswordAge;
        public uint Privilege;
        public IntPtr HomeDirectory;
        public IntPtr Comment;
        public uint Flags;
        public IntPtr ScriptPath;
    }
}
