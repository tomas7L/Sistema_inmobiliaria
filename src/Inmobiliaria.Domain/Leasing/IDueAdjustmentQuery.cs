namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// The due-adjustment worklist, read-model side of the rent-adjustment capability (design
/// Decision 8). Lives in Domain — a plain interface, no EF Core, Npgsql or WPF reference — so
/// the notifications change can depend on it without reaching into this capability's internals.
/// The only implementation is the Infrastructure adapter of the same name.
/// </summary>
public interface IDueAdjustmentQuery
{
    Task<IReadOnlyList<DueAdjustment>> GetDueAsync(DateOnly asOf, CancellationToken ct = default);
}
