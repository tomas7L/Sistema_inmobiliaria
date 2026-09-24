namespace Inmobiliaria.Domain.Access;

/// <summary>
/// The two PostgreSQL group roles a login role may belong to (spec Decision 4:
/// <c>inmobiliaria_admin</c> / <c>inmobiliaria_empleado</c>). This is read from the
/// database at login via <c>pg_has_role</c> and never stored on <see cref="AppUser"/>
/// (design Decision 5) — nothing on that row can drift from what the database enforces.
/// </summary>
public enum UserRole
{
    Admin,
    Empleado
}
