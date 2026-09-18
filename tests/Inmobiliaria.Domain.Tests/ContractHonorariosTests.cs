using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// Covers the honorarios (agency management fee) percentage stored on a contract. The rate is
/// negotiated with the owner but recorded per contract, because each issued receipt must be
/// reproducible from the contract that produced it even after the owner renegotiates.
/// Nullable, because the "Honorarios: Sin Asignar" entry in the agency's current tool is still
/// unexplained and may mean a genuinely unassigned fee.
/// </summary>
public class ContractHonorariosTests
{
    [Fact]
    public void Honorarios_StoresTheGivenPercentage()
    {
        // 8% is the rate on the real Del Lago receipts for owners VICO / VICO.
        var contract = ContractTestFactory.CreateActive(honorariosPercentage: 8m);

        Assert.Equal(8m, contract.HonorariosPercentage);
    }

    [Fact]
    public void Honorarios_WhenNotSupplied_IsNull()
    {
        var contract = ContractTestFactory.CreateActive();

        Assert.Null(contract.HonorariosPercentage);
    }

    [Fact]
    public void Honorarios_AtZero_IsAccepted()
    {
        var contract = ContractTestFactory.CreateActive(honorariosPercentage: 0m);

        Assert.Equal(0m, contract.HonorariosPercentage);
    }

    [Fact]
    public void Honorarios_AtOneHundred_IsAccepted()
    {
        var contract = ContractTestFactory.CreateActive(honorariosPercentage: 100m);

        Assert.Equal(100m, contract.HonorariosPercentage);
    }

    [Fact]
    public void Honorarios_BelowZero_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            ContractTestFactory.CreateActive(honorariosPercentage: -0.01m));
    }

    [Fact]
    public void Honorarios_AboveOneHundred_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            ContractTestFactory.CreateActive(honorariosPercentage: 100.01m));
    }
}
