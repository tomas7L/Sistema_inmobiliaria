using System.Text.RegularExpressions;

namespace Inmobiliaria.Domain.Units;

/// <summary>A unit's address as the operator typed it, on the way to being created or corrected.</summary>
public sealed record UnitDraft(
    string? Street,
    string? Number,
    string? City,
    string? Province,
    string? PostalCode,
    string? Floor = null,
    string? Apartment = null)
{
    /// <summary>
    /// Everything trimmed, a blank floor or apartment made absent and the postal code in capitals, as a CPA is printed: what
    /// gets stored once checked.
    /// </summary>
    public UnitDraft Normalized() => new(
        Street?.Trim() ?? string.Empty,
        Number?.Trim() ?? string.Empty,
        City?.Trim() ?? string.Empty,
        Province?.Trim() ?? string.Empty,
        PostalCode?.Trim().ToUpperInvariant() ?? string.Empty,
        Absent(Floor),
        Absent(Apartment));

    private static string? Absent(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>The field of the form a problem belongs to, in the form's order, so the screen shows it beside that field.</summary>
public enum UnitField
{
    Street,
    Number,
    Floor,
    Apartment,
    City,
    Province,
    PostalCode,
}

/// <summary>What is wrong with a field.</summary>
public enum UnitProblemKind
{
    /// <summary>A required field is blank.</summary>
    Missing,

    /// <summary>Longer than its field's maximum (spec Decision 48).</summary>
    TooLong,

    /// <summary>A province that is not one of <see cref="Provinces.All"/> (spec Decision 49).</summary>
    NotInList,

    /// <summary>A postal code in neither of its two forms, four digits or a CPA (spec Decision 49).</summary>
    Malformed,

    /// <summary>A CPA whose letter is not the chosen province's (spec Decision 49).</summary>
    OtherProvince,
}

/// <summary>One thing wrong with a draft, and the field it belongs to.</summary>
public sealed record UnitProblem(UnitField Field, UnitProblemKind Kind);

/// <summary>The form's rules, pure and without a database. An identical address needs data, so the adapter finds that.</summary>
public static partial class UnitDraftRules
{
    /// <summary>
    /// Every problem <paramref name="draft"/> has, in the form's order, each with its field (spec Requirements 27 and 28,
    /// Decision 44): what the form shows at once. Empty when the form can send it.
    /// </summary>
    public static IReadOnlyList<UnitProblem> Problems(UnitDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var clean = draft.Normalized();
        var problems = new List<UnitProblem>();

        AddText(problems, UnitField.Street, clean.Street!, required: true);
        AddText(problems, UnitField.Number, clean.Number!, required: true);
        AddText(problems, UnitField.Floor, clean.Floor ?? string.Empty, required: false);
        AddText(problems, UnitField.Apartment, clean.Apartment ?? string.Empty, required: false);
        AddText(problems, UnitField.City, clean.City!, required: true);

        if (clean.Province!.Length == 0)
        {
            problems.Add(new UnitProblem(UnitField.Province, UnitProblemKind.Missing));
        }
        else if (!Provinces.Contains(clean.Province))
        {
            problems.Add(new UnitProblem(UnitField.Province, UnitProblemKind.NotInList));
        }

        AddPostalCode(problems, clean.PostalCode!, clean.Province);

        return problems;
    }

    /// <summary>The port's guard: the refusal the draft's first problem earns, or null when it can be written.</summary>
    public static UnitRefusal? Check(UnitDraft draft) =>
        Problems(draft).FirstOrDefault() is { } problem
            ? problem.Kind switch
            {
                UnitProblemKind.Missing => UnitRefusal.AddressIncomplete,
                UnitProblemKind.TooLong => UnitRefusal.AddressTooLong,
                UnitProblemKind.Malformed => UnitRefusal.PostalCodeMalformed,
                UnitProblemKind.OtherProvince => UnitRefusal.PostalCodeOfAnotherProvince,
                _ => UnitRefusal.ProvinceNotInList,
            }
            : null;

    /// <summary>
    /// The most characters a free text field holds (spec Decision 48). The province has no length, being chosen, nor the
    /// postal code, whose two forms fix it.
    /// </summary>
    public static int MaximumLength(UnitField field) => field switch
    {
        UnitField.Street => 80,
        UnitField.Number => 10,
        UnitField.Floor or UnitField.Apartment => 20,
        UnitField.City => 60,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Neither the province nor the postal code has a length."),
    };

    /// <summary>
    /// Required, and in one of the two forms in use (spec Decision 49): the old four digits, from 1000, or the CPA, a letter,
    /// four digits and three letters, whose letter must be the province's. A province off the list is refused on its own, so
    /// the letter is not compared with it. Whether the code belongs to the city is not checked: no free official list says.
    /// </summary>
    private static void AddPostalCode(List<UnitProblem> problems, string postalCode, string? province)
    {
        if (postalCode.Length == 0)
        {
            problems.Add(new UnitProblem(UnitField.PostalCode, UnitProblemKind.Missing));
        }
        else if (!PostalCodeForm().IsMatch(postalCode))
        {
            problems.Add(new UnitProblem(UnitField.PostalCode, UnitProblemKind.Malformed));
        }
        else if (char.IsAsciiLetter(postalCode[0])
            && province is not null
            && Provinces.Contains(province)
            && postalCode[0] != Provinces.CpaLetter(province))
        {
            problems.Add(new UnitProblem(UnitField.PostalCode, UnitProblemKind.OtherProvince));
        }
    }

    /// <summary>Four digits from 1000, or a capital letter, four digits and three capital letters. Read after capitalizing.</summary>
    [GeneratedRegex(@"^([1-9][0-9]{3}|[A-Z][0-9]{4}[A-Z]{3})$")]
    private static partial Regex PostalCodeForm();

    private static void AddText(List<UnitProblem> problems, UnitField field, string text, bool required)
    {
        if (text.Length == 0)
        {
            if (required)
            {
                problems.Add(new UnitProblem(field, UnitProblemKind.Missing));
            }
        }
        else if (text.Length > MaximumLength(field))
        {
            problems.Add(new UnitProblem(field, UnitProblemKind.TooLong));
        }
    }
}

public static class UnitProblemText
{
    /// <summary>What the form shows beside the field (spec Decision 50: each problem in its own words).</summary>
    /// <param name="problem">The problem.</param>
    /// <param name="draft">The draft it was found in, whose province and postal code a CPA of another province names.</param>
    public static string OperatorText(this UnitProblem problem, UnitDraft draft)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(draft);

        return problem.Kind switch
        {
            UnitProblemKind.Missing => problem.Field switch
            {
                UnitField.Street => "Escriba la calle.",
                UnitField.Number => "Escriba el número. Si no tiene, escriba S/N.",
                UnitField.City => "Escriba la localidad.",
                UnitField.Province => "Elija la provincia.",
                UnitField.PostalCode => "Escriba el código postal.",
                _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "An optional field cannot be missing."),
            },
            UnitProblemKind.TooLong => $"{FieldName(problem.Field)} admite hasta {UnitDraftRules.MaximumLength(problem.Field)} caracteres.",
            UnitProblemKind.NotInList => "Elija una provincia de la lista.",
            UnitProblemKind.Malformed => "Escriba el código postal como 2322 o como S2322ABC.",
            UnitProblemKind.OtherProvince => OtherProvinceText(draft.Normalized()),
            _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "A problem without operator text."),
        };
    }

    private static string OtherProvinceText(UnitDraft clean) =>
        $"Un código postal de {clean.Province} empieza con {Provinces.CpaLetter(clean.Province!)}, no con {clean.PostalCode![0]}.";

    private static string FieldName(UnitField field) => field switch
    {
        UnitField.Street => "La calle",
        UnitField.Number => "El número",
        UnitField.Floor => "El piso",
        UnitField.Apartment => "El departamento o local",
        UnitField.City => "La localidad",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Neither the province nor the postal code has a length."),
    };
}
