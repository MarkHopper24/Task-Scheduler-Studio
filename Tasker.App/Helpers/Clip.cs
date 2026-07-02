using Windows.ApplicationModel.DataTransfer;

namespace Tasker_App.Helpers;

/// <summary>Clipboard helper that never throws. <see cref="Clipboard.SetContent"/> can fail
/// transiently when another process holds the clipboard open (CLIPBRD_E_CANT_OPEN); callers use
/// this so a copy action can't crash the UI. Returns whether the copy succeeded.</summary>
public static class Clip
{
    public static bool TrySetText(string? text)
    {
        try
        {
            var data = new DataPackage();
            data.SetText(text ?? string.Empty);
            Clipboard.SetContent(data);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
