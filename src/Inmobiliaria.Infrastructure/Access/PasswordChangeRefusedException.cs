using Inmobiliaria.Domain.Access;

namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// A password a user chose for themselves broke <see cref="PasswordRule"/>; nothing was sent to the
/// database. The message is the refusal's operator text and never contains a password.
/// </summary>
public sealed class PasswordChangeRefusedException : Exception
{
    public PasswordChangeRefusedException(PasswordRefusal refusal)
        : base(refusal.OperatorText())
    {
        Refusal = refusal;
    }

    public PasswordRefusal Refusal { get; }
}
