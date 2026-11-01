using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Desktop;

/// <summary>
/// The Windows clipboard, written only when the Admin presses Copy (gestion-de-usuarios spec Decision 12). A provisional
/// password is marked so Windows keeps it out of the clipboard history and the cloud clipboard, and it is cleared when
/// the Admin dismisses it, so it is not left behind once passed on. Checked by hand (design M1).
/// </summary>
public sealed class WpfClipboard : IClipboard
{
    public bool TrySetText(string text)
    {
        var data = new DataObject();
        data.SetText(text);

        // Documented clipboard formats Windows reads before keeping a copy in its history or syncing it.
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[4]));

        try
        {
            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (ExternalException)
        {
            // Another program holds the clipboard open.
            return false;
        }
    }

    public void ClearIfHolds(string text)
    {
        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == text)
            {
                Clipboard.Clear();
            }
        }
        catch (ExternalException)
        {
            // A clipboard held by another program is left as it is; the password is provisional anyway.
        }
    }
}
