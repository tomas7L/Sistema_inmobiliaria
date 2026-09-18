namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// Thrown when a <see cref="Contract"/>'s per-unit rent split would not sum to exactly
/// 100%, or would include a non-positive share.
/// </summary>
public sealed class RentSplitInvariantException : Exception
{
    public RentSplitInvariantException(string message)
        : base(message)
    {
    }
}
