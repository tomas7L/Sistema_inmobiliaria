using System.Text;
using System.Text.RegularExpressions;

namespace Inmobiliaria.Domain.Access;

/// <summary>
/// How a typed username is read, and which ones a new user may have (gestion-de-usuarios spec Decision 8).
/// The same reading is applied at login (spec Requirement 10), so a name is matched the way it was created.
/// </summary>
public static partial class Username
{
    /// <summary>
    /// Trims, composes to Unicode NFC and lowercases: "Nuñez " and a decomposed "n" + U+0303 both become
    /// "nuñez". Lowercasing can never point at a different user, because every username is lowercase
    /// (<see cref="AppUser"/>'s constructor and the <c>app_users</c> CHECK).
    /// </summary>
    public static string Normalize(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);
        return typed.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>
    /// 3 to 30 characters: lowercase <c>a</c>-<c>z</c>, <c>ñ</c>, digits, <c>.</c> and <c>_</c>, starting
    /// with a letter. Accented vowels are left out so "jose" and "josé" cannot be two logins.
    /// </summary>
    public static bool IsValid(string normalized) => normalized is not null && ValidShape().IsMatch(normalized);

    [GeneratedRegex("^[a-zñ][a-z0-9ñ._]{2,29}$")]
    private static partial Regex ValidShape();
}
