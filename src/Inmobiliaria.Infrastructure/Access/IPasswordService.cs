namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// Self password change only (spec "Self-Service Password Change Is Immediate; Admin Reset
/// Takes Effect Next Login"). Resetting a DIFFERENT user's password is
/// <see cref="IUserProvisioning.ResetPasswordAsync"/>'s job.
/// </summary>
public interface IPasswordService
{
    /// <summary>
    /// Changes the caller's own password. <paramref name="currentPassword"/> is required so
    /// this method can reject re-submitting it as the new one (spec "Re-Entering The
    /// Provisional Password Is Rejected") without ever querying the database for it — nothing
    /// stores a comparable verifier, so an ordinal string comparison against what the caller
    /// just supplied is the only mechanism this design offers (design Decision 6).
    /// </summary>
    Task ChangeOwnPasswordAsync(
        string currentPassword, string newPassword, CancellationToken ct = default);
}
