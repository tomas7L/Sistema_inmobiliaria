namespace Inmobiliaria.Presentation.Common;

/// <summary>The system clipboard, behind an interface so Presentation stays free of WPF and tests can see every write.</summary>
public interface IClipboard
{
    /// <summary>Writes <paramref name="text"/>; false when the clipboard could not be written, for example while another program holds it.</summary>
    bool TrySetText(string text);

    /// <summary>Clears the clipboard if it still holds exactly <paramref name="text"/>, and leaves anything else the user copied since.</summary>
    void ClearIfHolds(string text);
}
