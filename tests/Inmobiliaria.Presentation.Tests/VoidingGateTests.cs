using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Voiding;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// ui-de-cobranza spec tests 25 and 26 (pre-gate), design section 8: the screen offers a voiding by the age of
/// the pair and the operator's role as the domain does, and explains a withheld one in the domain's own text.
/// </summary>
public class VoidingGateTests
{
    private static readonly DateOnly Today = new(2026, 3, 31);

    [Theory]
    [InlineData(0, UserRole.Empleado, null)]
    [InlineData(7, UserRole.Empleado, null)]
    [InlineData(7, UserRole.Admin, null)]
    [InlineData(8, UserRole.Empleado, VoidingRefusal.AdminOnly)]
    [InlineData(8, UserRole.Admin, null)]
    [InlineData(30, UserRole.Admin, null)]
    [InlineData(31, UserRole.Admin, VoidingRefusal.WindowClosed)]
    [InlineData(31, UserRole.Empleado, VoidingRefusal.WindowClosed)]
    public void TheAgeAndTheRole_DecideWhatIsOffered(int daysOld, UserRole role, VoidingRefusal? withheld)
    {
        var gate = VoidingGate.For(Today.AddDays(-daysOld), Today, role, isAnnulled: false);

        Assert.Equal(withheld is null, gate.Offered);
        Assert.Equal(withheld?.OperatorText(), gate.Explanation);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Empleado)]
    public void AnAnnulledPair_IsOfferedToNobody(UserRole role)
    {
        var gate = VoidingGate.For(Today, Today, role, isAnnulled: true);

        Assert.False(gate.Offered);
        Assert.Equal(VoidingRefusal.AlreadyAnnulled.OperatorText(), gate.Explanation);
    }
}
