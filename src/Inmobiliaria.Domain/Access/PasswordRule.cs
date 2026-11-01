using System.Globalization;

namespace Inmobiliaria.Domain.Access;

/// <summary>Why a password a user chose for themselves was refused.</summary>
public enum PasswordRefusal
{
    SameAsCurrent,
    TooShort,
    EqualsUsername,
}

/// <summary>
/// The rule on every password a user chooses for themselves (gestion-de-usuarios spec Requirement 9): at
/// least <see cref="MinimumLength"/> characters as typed and not the username. No uppercase, digit or symbol
/// is demanded (NIST SP 800-63B), nothing is trimmed and there is no upper limit.
/// </summary>
public static class PasswordRule
{
    public const int MinimumLength = 8;

    /// <param name="username">
    /// The user's stored username (<see cref="AppUser.Username"/>, equal to the PostgreSQL role name), which is
    /// already lowercase.
    /// </param>
    /// <returns>The refusal, or null when the new password is acceptable.</returns>
    public static PasswordRefusal? Check(string username, string currentPassword, string newPassword)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newPassword);

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return PasswordRefusal.SameAsCurrent;
        }

        // Text elements, not UTF-16 units: an accented letter typed as a letter plus a combining mark, or
        // an emoji, counts as one character, as the user sees it.
        if (new StringInfo(newPassword).LengthInTextElements < MinimumLength)
        {
            return PasswordRefusal.TooShort;
        }

        if (string.Equals(newPassword.ToLowerInvariant(), username, StringComparison.Ordinal))
        {
            return PasswordRefusal.EqualsUsername;
        }

        return null;
    }
}

public static class PasswordRefusalText
{
    public static string OperatorText(this PasswordRefusal refusal) => refusal switch
    {
        PasswordRefusal.SameAsCurrent =>
            "La nueva contraseña debe ser distinta de la actual.",
        PasswordRefusal.TooShort =>
            $"La nueva contraseña debe tener al menos {PasswordRule.MinimumLength} caracteres.",
        PasswordRefusal.EqualsUsername =>
            "La nueva contraseña no puede ser igual al nombre de usuario.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A refusal without operator text."),
    };
}
