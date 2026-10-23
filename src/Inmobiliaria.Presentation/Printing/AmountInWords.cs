using System.Globalization;

namespace Inmobiliaria.Presentation.Printing;

/// <summary>
/// The recibo's total in Spanish words (spec Decision 36, design D14): "Son Pesos" + words + "con NN/100.-",
/// up to 999.999.999.999, "menos" before a negative total. No library: the rules are few and fixed. Before
/// "mil" or "millón" a final "uno" is shortened ("veintiún mil", "treinta y un mil", "un millón"), and 100 on
/// its own is "cien".
/// </summary>
public static class AmountInWords
{
    public const long Max = 999_999_999_999;

    private static readonly string[] Units =
        ["", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve"];

    private static readonly string[] Teens =
        ["diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve"];

    private static readonly string[] Twenties =
        ["veinte", "veintiuno", "veintidós", "veintitrés", "veinticuatro", "veinticinco", "veintiséis", "veintisiete", "veintiocho", "veintinueve"];

    private static readonly string[] Tens =
        ["", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa"];

    private static readonly string[] Hundreds =
        ["", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos", "setecientos", "ochocientos", "novecientos"];

    /// <summary>The whole line printed on the recibo.</summary>
    public static string Line(decimal amount)
    {
        var magnitude = Math.Abs(amount);
        var pesos = decimal.Truncate(magnitude);
        var centavos = (int)decimal.Round((magnitude - pesos) * 100m, MidpointRounding.AwayFromZero);
        var sign = amount < 0 ? "menos " : string.Empty;

        return $"Son Pesos {sign}{Words((long)pesos)} con {centavos.ToString("00", CultureInfo.InvariantCulture)}/100.-";
    }

    /// <summary>A non-negative whole number of pesos in words.</summary>
    public static string Words(long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(amount, Max);

        if (amount == 0)
        {
            return "cero";
        }

        var millions = (int)(amount / 1_000_000);
        var rest = (int)(amount % 1_000_000);
        var parts = new List<string>();

        if (millions == 1)
        {
            parts.Add("un millón");
        }
        else if (millions > 1)
        {
            parts.Add(BelowAMillion(millions, shorten: true) + " millones");
        }

        if (rest > 0)
        {
            parts.Add(BelowAMillion(rest, shorten: false));
        }

        return string.Join(' ', parts);
    }

    private static string BelowAMillion(int n, bool shorten)
    {
        var thousands = n / 1000;
        var rest = n % 1000;
        var parts = new List<string>();

        if (thousands == 1)
        {
            parts.Add("mil");
        }
        else if (thousands > 1)
        {
            parts.Add(BelowAThousand(thousands, shorten: true) + " mil");
        }

        if (rest > 0)
        {
            parts.Add(BelowAThousand(rest, shorten));
        }

        return string.Join(' ', parts);
    }

    private static string BelowAThousand(int n, bool shorten)
    {
        if (n == 100)
        {
            return "cien";
        }

        var hundreds = n / 100;
        var rest = n % 100;
        var tail = BelowAHundred(rest, shorten);

        return hundreds == 0 ? tail : rest == 0 ? Hundreds[hundreds] : $"{Hundreds[hundreds]} {tail}";
    }

    private static string BelowAHundred(int n, bool shorten)
    {
        string One() => shorten ? "un" : "uno";

        return n switch
        {
            1 => One(),
            < 10 => Units[n],
            < 20 => Teens[n - 10],
            21 when shorten => "veintiún",
            < 30 => Twenties[n - 20],
            _ when n % 10 == 0 => Tens[n / 10],
            _ when n % 10 == 1 => $"{Tens[n / 10]} y {One()}",
            _ => $"{Tens[n / 10]} y {Units[n % 10]}",
        };
    }
}
