namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// Composes the exact username Supabase's Supavisor pooler requires for a pooled connection:
/// <c>&lt;role&gt;.&lt;projectref&gt;</c> — a VERIFIED FACT proven from the office network
/// (spec Decision 9). The composed value is used only to open the connection; the session's
/// actual identity comes from a single <c>current_user</c> round trip immediately afterward
/// (design Decision 2), never from re-parsing this string, because the pooler strips the
/// suffix back off before PostgreSQL ever sees it.
/// </summary>
public static class SupavisorUsername
{
    public static string For(string role, string projectRef) => $"{role}.{projectRef}";
}
