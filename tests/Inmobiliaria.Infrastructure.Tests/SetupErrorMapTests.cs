using Inmobiliaria.Domain.GoLive;
using Inmobiliaria.Infrastructure.Persistence;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// ui-de-cobranza spec test 31, adapter half (design D25): the database's named errors become setup
/// refusals, keyed by SQLSTATE and constraint name, never by message text. Each table is scoped to its
/// own statement: a 42501 is "not Admin" only for the starting number, whose column grant raises it.
/// Anything not listed stays an error. No database needed.
/// </summary>
public class SetupErrorMapTests
{
    [Theory]
    [InlineData("P0001", "contracts_cutover_locked", SetupRefusal.CutoverLocked)]
    [InlineData("P0001", "contracts_cutover_admin_only", SetupRefusal.CutoverAdminOnly)]
    public void TheCutoverStatementsErrors_AreRefusals(string sqlState, string constraint, SetupRefusal expected) =>
        Assert.Equal(expected, SetupErrorMap.ForCutover(sqlState, constraint));

    [Theory]
    [InlineData("P0001", "recibo_numbering_cutover_locked", SetupRefusal.StartingNumberLocked)]
    [InlineData("23514", "ck_recibo_numbering_starting_positive", SetupRefusal.StartingNumberOutOfRange)]
    [InlineData("42501", null, SetupRefusal.StartingNumberAdminOnly)]
    public void TheStartingNumberStatementsErrors_AreRefusals(string sqlState, string? constraint, SetupRefusal expected) =>
        Assert.Equal(expected, SetupErrorMap.ForStartingNumber(sqlState, constraint));

    [Theory]
    [InlineData("42501", null)]
    [InlineData("P0001", "recibo_numbering_cutover_locked")]
    [InlineData("P0001", "contracts_fee_plan_locked")]
    [InlineData("23505", "contracts_cutover_locked")]
    [InlineData(null, null)]
    public void AnythingElse_OnTheCutoverStatement_IsNotARefusal(string? sqlState, string? constraint) =>
        Assert.Null(SetupErrorMap.ForCutover(sqlState, constraint));

    [Theory]
    [InlineData("P0001", "contracts_cutover_locked")]
    [InlineData("P0001", "contracts_cutover_admin_only")]
    [InlineData("23505", null)]
    [InlineData("P0001", "ck_recibo_numbering_starting_positive")]
    [InlineData(null, null)]
    public void AnythingElse_OnTheStartingNumberStatement_IsNotARefusal(string? sqlState, string? constraint) =>
        Assert.Null(SetupErrorMap.ForStartingNumber(sqlState, constraint));
}
