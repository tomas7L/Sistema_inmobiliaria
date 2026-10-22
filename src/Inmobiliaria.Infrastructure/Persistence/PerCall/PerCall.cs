using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// The one helper every per-call wrapper forwards through (design D2, D3): a context is created for the
/// call, handed to the real adapter and disposed when the call ends, whether it returned or threw. A
/// context is not safe to use from two operations at once, and a scope per screen would keep one alive
/// for minutes, so each preview, commit, voiding or setup call gets its own.
/// </summary>
internal static class PerCall
{
    public static async Task<T> RunAsync<T>(
        ISessionDbContextFactory factory, Func<InmobiliariaDbContext, Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(body);

        await using var context = factory.Create();
        return await body(context);
    }
}
