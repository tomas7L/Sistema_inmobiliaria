namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// The tenant's one-off honorarios contractuales, split into equal fixed instalments that ride with
/// the first rent periods of the contract (cobranza-y-recibos spec Decision 32; design D19).
///
/// A plan is an amount AND a count, never one without the other, which is why the only constructor
/// takes both: "an amount without a count" cannot be built. A contract with no plan is represented
/// by the absence of a plan, not by a plan of zero. The database carries the same rules as check
/// constraints, for anything that bypasses this type.
///
/// The amount is the figure of EACH instalment and it is fixed: it never adjusts with the rent.
/// </summary>
public sealed record ContractualFeePlan
{
    /// <summary>The most instalments a plan may be split into.</summary>
    public const int MaxInstalments = 6;

    /// <summary>The amount of each instalment. Strictly positive.</summary>
    public decimal Amount { get; }

    /// <summary>How many instalments there are, from 1 to <see cref="MaxInstalments"/>.</summary>
    public int Instalments { get; }

    public ContractualFeePlan(decimal amount, int instalments)
    {
        if (amount <= 0m)
        {
            throw new ArgumentException("A fee instalment must be a positive amount.", nameof(amount));
        }

        if (instalments is < 1 or > MaxInstalments)
        {
            throw new ArgumentException(
                $"A fee plan has between 1 and {MaxInstalments} instalments.", nameof(instalments));
        }

        Amount = amount;
        Instalments = instalments;
    }
}
