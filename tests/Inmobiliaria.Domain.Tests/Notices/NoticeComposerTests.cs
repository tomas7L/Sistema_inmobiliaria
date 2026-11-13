using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Notices;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// notificaciones spec Test 6 (Requirement 2): the email states the date its figures are for, says recargo grows after it,
/// names what the figures cover in the Mora screen's words, and never presents the total and the recargo as two amounts to add.
/// </summary>
public class NoticeComposerTests
{
    private const string Agency = "Del Lago Servicios Inmobiliarios";

    private static TenantNotice NoticeOn(DateOnly day, decimal total = 1_054_800m, decimal recargo = 54_800m, int daysOverdue = 12) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "C-1A2B",
            "Perez, Maria",
            "Belgrano 123, Depto 2",
            "maria.perez@example.com",
            day,
            total,
            recargo,
            daysOverdue,
            TenantNotice.MonthOf(day));

    [Fact]
    public void TheEmail_StatesItsDate_AndThatRecargoGrowsAfterIt()
    {
        var mail = NoticeComposer.Compose(NoticeOn(new DateOnly(2026, 12, 3)), Agency);

        Assert.Contains("3 de diciembre de 2026", mail.Subject);
        Assert.Contains("Al 3 de diciembre de 2026, el total adeudado", mail.Body);
        Assert.Contains("Después de esa fecha", mail.Body);
        Assert.Contains("el recargo por mora crece cada día de atraso", mail.Body);
    }

    [Fact]
    public void TheEmail_GoesToTheTenant_NamesTheContract_AndSignsAsTheAgency()
    {
        var notice = NoticeOn(new DateOnly(2026, 12, 3));

        var mail = NoticeComposer.Compose(notice, Agency);

        Assert.Equal("maria.perez@example.com", mail.To);
        Assert.StartsWith(Agency, mail.Subject);
        Assert.Contains("Perez, Maria", mail.Body);
        Assert.Contains("C-1A2B", mail.Body);
        Assert.Contains("Belgrano 123, Depto 2", mail.Body);
        Assert.EndsWith(Agency, mail.Body.TrimEnd());
    }

    /// <summary>
    /// The greeting names nobody, and the tenant is named in the contract's sentence in the desk search's text (spec
    /// Requirement 3), so "Hola, Perez, Maria:" never reads as a greeting (tasks 4.2, Tomás 2026-11-13).
    /// </summary>
    [Fact]
    public void TheGreeting_NamesNobody_AndTheContractSentence_NamesTheTenant()
    {
        var mail = NoticeComposer.Compose(NoticeOn(new DateOnly(2026, 12, 3)), Agency);

        Assert.StartsWith("Hola:\n\n", mail.Body);
        Assert.Contains($"Le escribimos de {Agency} por el contrato C-1A2B de Perez, Maria (Belgrano 123, Depto 2).", mail.Body);
    }

    [Fact]
    public void TheEmail_NamesWhatTheFiguresCover_InTheMoraScreensWords()
    {
        var mail = NoticeComposer.Compose(NoticeOn(new DateOnly(2026, 12, 3)), Agency);

        Assert.Contains(BalanceCoverage.Sentence, mail.Body);
    }

    [Fact]
    public void TheRecargo_IsNamedInsideTheTotal_NeverAsAnAmountToAdd()
    {
        var mail = NoticeComposer.Compose(NoticeOn(new DateOnly(2026, 12, 3)), Agency);

        Assert.Contains("el total adeudado es de $ 1.054.800", mail.Body);
        Assert.Contains("Ese total ya incluye $ 54.800 de recargo por mora", mail.Body);
        Assert.Contains("12 días de atraso", mail.Body);

        // Neither the sum nor the remainder appears: the recargo is inside the total, not beside it.
        Assert.DoesNotContain("1.109.600", mail.Body);
        Assert.DoesNotContain("1.000.000", mail.Body);
    }

    [Fact]
    public void WithNoRecargo_NoRecargoFigureIsStated_ButTheDateAndTheGrowthAre()
    {
        var mail = NoticeComposer.Compose(NoticeOn(new DateOnly(2026, 12, 3), total: 500_000m, recargo: 0m, daysOverdue: 0), Agency);

        Assert.Contains("el total adeudado es de $ 500.000", mail.Body);
        Assert.DoesNotContain("Ese total ya incluye", mail.Body);
        Assert.DoesNotContain("días de atraso", mail.Body);
        Assert.Contains("Al 3 de diciembre de 2026, el total adeudado", mail.Body);
        Assert.Contains("el recargo por mora crece cada día de atraso", mail.Body);
    }

    [Fact]
    public void Cents_AreWrittenWithAComma()
    {
        var mail = NoticeComposer.Compose(NoticeOn(new DateOnly(2026, 12, 3), total: 1_054_800.5m, recargo: 54_800.5m), Agency);

        Assert.Contains("$ 1.054.800,5", mail.Body);
        Assert.Contains("$ 54.800,5", mail.Body);
    }

    [Fact]
    public void ABlankAgencyName_IsRefused() =>
        Assert.ThrowsAny<ArgumentException>(() => NoticeComposer.Compose(NoticeOn(new DateOnly(2026, 12, 3)), " "));
}
