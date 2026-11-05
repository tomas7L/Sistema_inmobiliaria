using Inmobiliaria.Domain.Units;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-alquileres spec Requirement 5: the address a unit requires, and a type that cannot change (Test 9).</summary>
public class UnitDraftTests
{
    [Fact]
    public void ACompleteAddress_IsAccepted_WithFloorAndApartmentOptional()
    {
        Assert.Null(UnitDraftRules.Check(new UnitDraft("Alem", "1258", "Sunchales", "Santa Fe", "2322")));
    }

    [Theory]
    [InlineData(" ", "1258", "Sunchales", "Santa Fe", "2322")]
    [InlineData("Alem", null, "Sunchales", "Santa Fe", "2322")]
    [InlineData("Alem", "1258", "", "Santa Fe", "2322")]
    [InlineData("Alem", "1258", "Sunchales", null, "2322")]
    [InlineData("Alem", "1258", "Sunchales", "Santa Fe", "  ")]
    public void AnyRequiredPartMissing_IsRefused(string? street, string? number, string? city, string? province, string? postalCode)
    {
        Assert.Equal(UnitRefusal.AddressIncomplete, UnitDraftRules.Check(new UnitDraft(street, number, city, province, postalCode)));
    }

    [Fact]
    public void Normalized_TrimsEverything_AndMakesABlankFloorOrApartmentAbsent()
    {
        var clean = new UnitDraft(" Alem ", " 1258", "Sunchales ", " Santa Fe", "2322 ", "  ", " B ").Normalized();

        Assert.Equal(new UnitDraft("Alem", "1258", "Sunchales", "Santa Fe", "2322", null, "B"), clean);
    }

    /// <summary>Spec Test 9: nothing on a unit can rewrite its type.</summary>
    [Fact]
    public void AUnitsType_HasNoSetter()
    {
        Assert.False(typeof(Unit).GetProperty(nameof(Unit.UnitType))!.CanWrite);
    }
}
