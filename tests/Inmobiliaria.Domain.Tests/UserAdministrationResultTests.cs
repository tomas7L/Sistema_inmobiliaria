using Inmobiliaria.Domain.Access;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-usuarios spec Requirements 6 and 8: what the user screen's port hands back.</summary>
public class UserAdministrationResultTests
{
    [Fact]
    public void ACredential_NeverPrintsItsPassword()
    {
        var credential = new ProvisionalCredential("sofia", "lago-sol-pan-48017");

        Assert.DoesNotContain("lago-sol-pan-48017", credential.ToString());
        Assert.Contains("sofia", credential.ToString());
        Assert.DoesNotContain("lago-sol-pan-48017", UserAdminResult.Issued(credential).ToString());
    }

    [Fact]
    public void Results_SayWhetherTheySucceeded()
    {
        Assert.True(UserAdminResult.Success.Succeeded);
        Assert.Null(UserAdminResult.Success.Credential);
        Assert.True(UserAdminResult.Issued(new ProvisionalCredential("sofia", "x")).Succeeded);

        var refused = UserAdminResult.Refused(UserAdminRefusal.UsernameTaken);
        Assert.False(refused.Succeeded);
        Assert.Null(refused.Credential);
    }

    [Fact]
    public void EveryRefusal_HasItsOwnOperatorText()
    {
        var texts = Enum.GetValues<UserAdminRefusal>().Select(r => r.OperatorText()).ToList();

        Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.Equal(texts.Count, texts.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TheForeignRoleRefusal_SaysAnotherAdminCreatedTheUser()
    {
        Assert.Contains("otro Administrador", UserAdminRefusal.CreatedByAnotherAdmin.OperatorText());
    }
}
