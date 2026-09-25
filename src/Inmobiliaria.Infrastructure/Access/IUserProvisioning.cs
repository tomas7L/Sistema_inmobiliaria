namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// Admin-only user governance (spec "Admin Provisions and Deactivates Users From Inside the
/// Application"). Creates ONLY Empleado accounts — see <see cref="CreateUserAsync"/>'s remarks
/// for why creating an Admin in-app is explicitly out of scope for this slice (design Decision 9;
/// tasks.md task 3.12's proven finding; the residual "H.3" decision point this type resolves).
/// </summary>
public interface IUserProvisioning
{
    /// <summary>
    /// Creates a new Empleado login role and its matching <c>app_users</c> row as one unit
    /// (spec test 9). The new user's password must be changed at first login
    /// (<c>MustChangePassword = true</c> — spec test 15's database half). Returns the new
    /// <see cref="Domain.Access.AppUser"/>'s id.
    /// </summary>
    /// <remarks>
    /// <b>Why this never provisions an Admin.</b> <c>app_create_login_role</c> (the migration's
    /// SQL function this method calls) is deliberately NOT <c>SECURITY DEFINER</c> (design
    /// Decision 3): it runs as the calling Admin, and its internal
    /// <c>GRANT %I TO %I</c> for the new role carries no <c>WITH ADMIN OPTION</c> clause — for
    /// EITHER group role, regardless of which one is granted. Task 3.12 proved, against a real
    /// engine, that <c>ADMIN OPTION</c> does not inherit through nested group membership. The
    /// consequence is structural, not a policy this type chooses to enforce: a login role this
    /// method creates can never itself call <c>app_create_login_role</c> or
    /// <c>app_set_role_password</c> against a DIFFERENT role successfully — it would always fail
    /// with <c>42501</c>, exactly like the second Admin in
    /// <c>RolePermissionTests.AdminOptionInheritance_ProvenNotAssumed</c>. Provisioning an Admin
    /// in-app would therefore create an Admin who holds every ordinary Admin table grant but is
    /// permanently unable to do the one thing that is supposed to distinguish her from an
    /// Empleado. Rather than ship a role that looks privileged and is not, this method creates
    /// only Empleado accounts — exactly what the spec's own "Admin creates a new employee"
    /// scenario shows. Creating an additional Admin remains
    /// <c>docs/runbooks/bootstrap-first-admin.md</c>'s job: a human, holding the database owner
    /// credential, grants <c>CREATEROLE</c> plus <c>ADMIN OPTION</c> on BOTH group roles directly
    /// to that new Admin login role — the only grant shape task 3.12 proved actually works.
    /// </remarks>
    Task<Guid> CreateUserAsync(string username, string password, string displayName, CancellationToken ct = default);

    /// <summary>
    /// Resets a DIFFERENT user's password (spec "Self-Service Password Change Is Immediate;
    /// Admin Reset Takes Effect Next Login"). Takes effect only the next time that user
    /// authenticates — it never disturbs a session that user already has open (spec test 14),
    /// because it never touches that user's already-open connection at all; only PostgreSQL's
    /// stored password changes. Always sets <c>MustChangePassword = true</c> (spec test 16).
    /// Succeeds only when the CALLING Admin is the same one who originally created
    /// <paramref name="username"/> via <see cref="CreateUserAsync"/> — PostgreSQL restricts
    /// <c>ALTER ROLE ... PASSWORD</c> on an existing role to that role's creator (or a
    /// superuser); discovered running this slice's own tests, consistent with this project
    /// having exactly one Admin.
    /// </summary>
    Task ResetPasswordAsync(string username, string newPassword, CancellationToken ct = default);

    /// <summary>
    /// Deactivates a user: <c>ALTER ROLE ... NOLOGIN</c> plus <c>AppUser.IsActive = false</c>
    /// (spec "Deactivate, Never Delete"). Neither the login role nor the <c>app_users</c> row is
    /// ever dropped, so every FK already naming this user keeps resolving to it (spec test 11,
    /// 31). Same caller constraint as <see cref="ResetPasswordAsync"/>.
    /// </summary>
    Task DeactivateAsync(string username, CancellationToken ct = default);
}
