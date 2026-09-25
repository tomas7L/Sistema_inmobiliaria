namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// Self password change only (spec "Self-Service Password Change Is Immediate; Admin Reset
/// Takes Effect Next Login"). Nothing here resets a DIFFERENT user's password — that is
/// provisioning's job (PR 4, out of this slice's scope).
/// </summary>
public interface IPasswordService
{
    Task ChangeOwnPasswordAsync(string newPassword, CancellationToken ct = default);
}
