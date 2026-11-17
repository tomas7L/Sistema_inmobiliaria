using System.Globalization;
using System.Text;

namespace Inmobiliaria.Domain.Shared;

/// <summary>
/// How every search in the system compares text (ui-de-cobranza design D17): lower case, accents removed, surrounding space
/// trimmed, so "Demaría" and "demaria" match. Shared by the database searches and the screens' pickers.
/// </summary>
public static class TextFold
{
    /// <summary>"Demaría" and "demaria" fold to the same text.</summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
