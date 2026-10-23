using System.Globalization;

namespace Inmobiliaria.Presentation.Recibos;

/// <summary>How a screen reads a typed recibo number, and what it says when nothing has it (spec Requirement 13).</summary>
public static class ReciboSearch
{
    public const string TypeANumber = "Escriba el número de uno de los dos recibos.";

    public static string NotFound(long number) => $"No hay ningún recibo con el número {number}.";

    /// <summary>Digits only; the reader itself returns nothing for a number the column cannot hold.</summary>
    public static bool TryParse(string? text, out long number) =>
        long.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out number);
}
