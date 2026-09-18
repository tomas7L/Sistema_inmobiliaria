namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// Names one <see cref="Indices.EconomicIndex"/> an <see cref="AdjustmentClause"/> references,
/// with its display order. Holds only the id — never an <see cref="Indices.EconomicIndex"/>
/// reference — identical in shape to <see cref="ContractUnit"/> holding a unit id, so the
/// clause never reaches into another aggregate. Constructed only through
/// <see cref="AdjustmentClause"/>'s constructor.
/// </summary>
public sealed class AdjustmentClauseIndex
{
    public Guid AdjustmentClauseId { get; }
    public Guid EconomicIndexId { get; }
    public int Ordinal { get; }

    internal AdjustmentClauseIndex(Guid adjustmentClauseId, Guid economicIndexId, int ordinal)
    {
        if (ordinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, "Ordinal cannot be negative.");
        }

        AdjustmentClauseId = adjustmentClauseId;
        EconomicIndexId = economicIndexId;
        Ordinal = ordinal;
    }
}
