namespace Inmobiliaria.Domain.Access;

/// <summary>
/// What the Admin's Usuarios screen does (gestion-de-usuarios spec Requirements 1 to 8): list every user, and
/// create, reset, deactivate or reactivate an Empleado. Actions take the row's id, so a stale list can never act
/// on a different person. Anything the Admin can cause comes back as a <see cref="UserAdminRefusal"/>, never as
/// an exception; the database GRANTs remain the enforcement (<c>access-control</c>).
/// </summary>
public interface IUserAdministration
{
    Task<IReadOnlyList<UserListing>> ListAsync(CancellationToken ct = default);

    /// <summary>Creates an Empleado with a generated provisional password, returned in the result.</summary>
    Task<UserAdminResult> CreateEmpleadoAsync(string typedUsername, string displayName, CancellationToken ct = default);

    /// <summary>Gives an active Empleado a new generated provisional password.</summary>
    Task<UserAdminResult> ResetPasswordAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Stops an Empleado from logging in. Deactivating a deactivated user succeeds and changes nothing.</summary>
    Task<UserAdminResult> DeactivateAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Restores a deactivated Empleado's login with a new generated provisional password, in one transaction.</summary>
    Task<UserAdminResult> ReactivateAsync(Guid userId, CancellationToken ct = default);
}

/// <param name="Role">Null for a user in neither group role.</param>
public sealed record UserListing(
    Guid Id, string Username, string DisplayName, UserRole? Role, bool IsActive, bool MustChangePassword);

/// <summary>A generated password and the username it belongs to, shown once to the Admin.</summary>
public sealed record ProvisionalCredential(string Username, string Password)
{
    /// <summary>Never includes the password, so a credential printed into any message or log carries no secret.</summary>
    public override string ToString() => $"ProvisionalCredential {{ Username = {Username} }}";
}

public enum UserAdminRefusal
{
    UsernameInvalid,
    UsernameTaken,
    DisplayNameMissing,
    UserNotFound,

    /// <summary>The user is an Admin, or belongs to neither group role: only Empleados are acted on.</summary>
    NotAnEmpleado,

    /// <summary>A reset of a deactivated user: reactivate instead.</summary>
    AlreadyDeactivated,

    AlreadyActive,

    /// <summary>PostgreSQL lets an Admin change only roles that Admin created (<c>user-registry</c>).</summary>
    CreatedByAnotherAdmin,

    /// <summary>The session is not an Admin's.</summary>
    AdminOnly,

    /// <summary>An Admin login without the runbook's CREATEROLE and ADMIN OPTION grants cannot create a user.</summary>
    AdminGrantsMissing,
}

public sealed record UserAdminResult(UserAdminRefusal? Refusal, ProvisionalCredential? Credential)
{
    public static readonly UserAdminResult Success = new(null, null);

    public bool Succeeded => Refusal is null;

    public static UserAdminResult Issued(ProvisionalCredential credential) => new(null, credential);

    public static UserAdminResult Refused(UserAdminRefusal refusal) => new(refusal, null);
}

public static class UserAdminRefusalText
{
    public static string OperatorText(this UserAdminRefusal refusal) => refusal switch
    {
        UserAdminRefusal.UsernameInvalid =>
            "El nombre de usuario debe tener entre 3 y 30 caracteres: letras sin tilde (se permite la ñ), números, punto o guion bajo, y empezar con una letra.",
        UserAdminRefusal.UsernameTaken =>
            "Ese nombre de usuario ya fue usado y no se puede volver a asignar. Elija otro.",
        UserAdminRefusal.DisplayNameMissing =>
            "Escriba el nombre y apellido del usuario.",
        UserAdminRefusal.UserNotFound =>
            "El usuario ya no existe. Actualice la lista.",
        UserAdminRefusal.NotAnEmpleado =>
            "Desde esta pantalla sólo se pueden modificar usuarios Empleado.",
        UserAdminRefusal.AlreadyDeactivated =>
            "El usuario está desactivado. Para que vuelva a entrar, reactívelo.",
        UserAdminRefusal.AlreadyActive =>
            "El usuario ya está activo.",
        UserAdminRefusal.CreatedByAnotherAdmin =>
            "Este usuario lo creó otro Administrador, y sólo quien lo creó puede modificarlo.",
        UserAdminRefusal.AdminOnly =>
            "Sólo un Administrador puede administrar usuarios.",
        UserAdminRefusal.AdminGrantsMissing =>
            "Su usuario Administrador no tiene los permisos para crear usuarios. Hay que completar su alta según el procedimiento de Administradores.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A refusal without operator text."),
    };
}
