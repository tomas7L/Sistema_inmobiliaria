using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Domain.Tests;

public class ContractPartyAssignmentTests
{
    [Fact]
    public void AssignParty_TwoLessors_BothAccepted()
    {
        var contract = ContractTestFactory.CreateActive();
        var lessorA = ContractTestFactory.CreateParty("Leslie", "Vico");
        var lessorB = ContractTestFactory.CreateParty("Alejandra", "Vico");

        contract.AssignParty(lessorA, PartyRole.Lessor);
        contract.AssignParty(lessorB, PartyRole.Lessor);

        Assert.Equal(2, contract.Parties.Count(p => p.Role == PartyRole.Lessor));
    }

    [Fact]
    public void AssignParty_SecondTenant_IsRejected()
    {
        var contract = ContractTestFactory.CreateActive();
        contract.AssignParty(ContractTestFactory.CreateParty("Juan", "Perez"), PartyRole.Tenant);

        Assert.Throws<InvalidOperationException>(
            () => contract.AssignParty(ContractTestFactory.CreateParty("Maria", "Gomez"), PartyRole.Tenant));
    }

    [Fact]
    public void AssignParty_ZeroOrTwoCodebtors_BothAccepted()
    {
        var contractWithNoCodebtors = ContractTestFactory.CreateActive();
        Assert.DoesNotContain(contractWithNoCodebtors.Parties, p => p.Role == PartyRole.Codebtor);

        var contractWithTwoCodebtors = ContractTestFactory.CreateActive();
        contractWithTwoCodebtors.AssignParty(ContractTestFactory.CreateParty("Ana", "Lopez"), PartyRole.Codebtor);
        contractWithTwoCodebtors.AssignParty(ContractTestFactory.CreateParty("Luis", "Diaz"), PartyRole.Codebtor);

        Assert.Equal(2, contractWithTwoCodebtors.Parties.Count(p => p.Role == PartyRole.Codebtor));
    }

    [Fact]
    public void AssignParty_SamePartyAsLessorAndCodebtor_BothAccepted()
    {
        var contract = ContractTestFactory.CreateActive();
        var party = ContractTestFactory.CreateParty("Carlos", "Ruiz");

        contract.AssignParty(party, PartyRole.Lessor);
        contract.AssignParty(party, PartyRole.Codebtor);

        Assert.Equal(2, contract.Parties.Count(p => p.PartyId == party.Id));
    }

    [Fact]
    public void AssignParty_DuplicatePartyAndRole_IsRejected()
    {
        var contract = ContractTestFactory.CreateActive();
        var party = ContractTestFactory.CreateParty("Sofia", "Torres");
        contract.AssignParty(party, PartyRole.Codebtor);

        Assert.Throws<InvalidOperationException>(
            () => contract.AssignParty(party, PartyRole.Codebtor));
    }
}
