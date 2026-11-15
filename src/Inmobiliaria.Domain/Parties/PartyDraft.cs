using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Inmobiliaria.Domain.Parties;

/// <summary>A person as the operator typed them, on the way to being created or corrected.</summary>
public sealed record PartyDraft(
    string? FirstName,
    string? LastName,
    string? Dni = null,
    string? Cuil = null,
    string? Email = null,
    string? PhoneNumber = null,
    string? Domicile = null)
{
    /// <summary>Names trimmed, identifiers normalized and anything blank made absent: what gets stored once checked.</summary>
    public PartyDraft Normalized() => new(
        FirstName?.Trim() ?? string.Empty,
        LastName?.Trim() ?? string.Empty,
        PartyIdentifiers.NormalizeDni(Dni),
        PartyIdentifiers.NormalizeCuil(Cuil),
        Absent(Email),
        Absent(PhoneNumber),
        Absent(Domicile));

    private static string? Absent(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>The field of the form a problem belongs to, so the screen shows it beside that field.</summary>
public enum PartyField
{
    FirstName,
    LastName,
    Dni,
    Cuil,
    Phone,
    Email,
    Domicile,

    /// <summary>Phone and email together: at least one of the two (spec Decision 47).</summary>
    Contact,
}

/// <summary>One thing wrong with a draft, and the field it belongs to.</summary>
public sealed record PartyProblem(PartyField Field, PartyRefusal Refusal);

/// <summary>Something that is likely a typing error but may be true, so the operator may save it anyway (Decisions 46, 47).</summary>
public enum PartyWarning
{
    /// <summary>The CUIL or CUIT does not carry the DNI as its eight middle digits.</summary>
    CuilDoesNotCarryDni,

    /// <summary>A DNI was given and no CUIL or CUIT.</summary>
    DniWithoutCuil,
}

/// <summary>The form's rules, pure and without a database (design D19). Duplicates need data, so the adapter finds those.</summary>
public static partial class PartyDraftRules
{
    public const int MaximumNameLength = 60;
    public const int MaximumDomicileLength = 120;
    private const int MinimumPhoneDigits = 8;
    private const int MaximumPhoneDigits = 15;

    /// <summary>
    /// Every problem <paramref name="draft"/> has, in the form's order, each with its field (spec Requirement 26, Decision 44):
    /// what the form shows at once. Empty when the form can send it.
    /// </summary>
    public static IReadOnlyList<PartyProblem> Problems(PartyDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var clean = draft.Normalized();
        var problems = new List<PartyProblem>();

        AddName(problems, PartyField.FirstName, clean.FirstName!);
        AddName(problems, PartyField.LastName, clean.LastName!);

        if (clean.Dni is not null && !PartyIdentifiers.IsValidDni(clean.Dni))
        {
            problems.Add(new PartyProblem(PartyField.Dni, PartyRefusal.DniInvalid));
        }

        if (clean.Cuil is not null && !PartyIdentifiers.IsValidCuil(clean.Cuil))
        {
            var elevenDigits = clean.Cuil.Length == 11 && clean.Cuil.All(char.IsAsciiDigit);
            problems.Add(new PartyProblem(PartyField.Cuil, elevenDigits ? PartyRefusal.CuilInvalid : PartyRefusal.CuilWrongLength));
        }

        if (clean.PhoneNumber is not null && !IsValidPhone(clean.PhoneNumber))
        {
            problems.Add(new PartyProblem(PartyField.Phone, PartyRefusal.PhoneInvalid));
        }

        if (clean.Email is not null && !IsValidEmail(clean.Email))
        {
            problems.Add(new PartyProblem(PartyField.Email, PartyRefusal.EmailInvalid));
        }

        if (clean.Domicile is { Length: > MaximumDomicileLength })
        {
            problems.Add(new PartyProblem(PartyField.Domicile, PartyRefusal.DomicileTooLong));
        }

        if (clean.PhoneNumber is null && clean.Email is null)
        {
            problems.Add(new PartyProblem(PartyField.Contact, PartyRefusal.ContactMissing));
        }

        return problems;
    }

    /// <summary>
    /// What may be a typing error but may be true (Decisions 46 and 47), for a draft with no problems: the operator confirms it.
    /// </summary>
    public static IReadOnlyList<PartyWarning> Warnings(PartyDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var clean = draft.Normalized();
        if (clean.Dni is null || !PartyIdentifiers.IsValidDni(clean.Dni))
        {
            return [];
        }

        if (clean.Cuil is null)
        {
            return [PartyWarning.DniWithoutCuil];
        }

        return PartyIdentifiers.IsValidCuil(clean.Cuil) && clean.Cuil[2..10] != clean.Dni.PadLeft(8, '0')
            ? [PartyWarning.CuilDoesNotCarryDni]
            : [];
    }

    /// <summary>
    /// The port's guard: the first problem of the draft, or the missing identifiers not yet accepted, or null when it can be
    /// written. The phone-or-email rule is left to the form (tasks 12.3e): it only says what to ask, not what may be stored.
    /// </summary>
    public static PartyRefusal? Check(PartyDraft draft, bool acceptNoIdentifier)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (Problems(draft).FirstOrDefault(p => p.Field != PartyField.Contact) is { } problem)
        {
            return problem.Refusal;
        }

        var normalized = draft.Normalized();
        return normalized.Dni is null && normalized.Cuil is null && !acceptNoIdentifier
            ? PartyRefusal.NoIdentifierNotAccepted
            : null;
    }

    /// <summary>
    /// The email rule: the check the notices use before sending, which an address must pass to be sent to, plus a dot inside
    /// its domain, which that check does not require and whose absence is the commonest typing error (Decision 54).
    /// </summary>
    public static bool IsValidEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);

        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
        {
            return false;
        }

        var domain = parsed.Host;
        var dot = domain.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && !domain.EndsWith('.');
    }

    private static void AddName(List<PartyProblem> problems, PartyField field, string name)
    {
        if (name.Length == 0)
        {
            problems.Add(new PartyProblem(field, PartyRefusal.NameMissing));
        }
        else if (name.Length > MaximumNameLength)
        {
            problems.Add(new PartyProblem(field, PartyRefusal.NameTooLong));
        }
        else if (!NameCharacters().IsMatch(name) || !name.Any(char.IsLetter))
        {
            problems.Add(new PartyProblem(field, PartyRefusal.NameInvalid));
        }
    }

    private static bool IsValidPhone(string phone)
    {
        var digits = phone.Count(char.IsAsciiDigit);
        return PhoneCharacters().IsMatch(phone) && digits is >= MinimumPhoneDigits and <= MaximumPhoneDigits;
    }

    /// <summary>Letters of any language with their accents, space, both apostrophes, hyphen and dot.</summary>
    [GeneratedRegex(@"^[\p{L}\p{M} '’.\-]+$")]
    private static partial Regex NameCharacters();

    /// <summary>An optional leading plus, then digits, spaces, hyphens and parentheses.</summary>
    [GeneratedRegex(@"^\+?[0-9 ()\-]+$")]
    private static partial Regex PhoneCharacters();
}
