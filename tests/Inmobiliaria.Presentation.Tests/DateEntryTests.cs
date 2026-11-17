using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// Dates typed short (Tomás, 2026-11-17: "es molesto tener que escribir la fecha exacta"): day, month and year with any
/// separator or none, a two-digit year, and on a start or an end, the month and year alone.
/// </summary>
public class DateEntryTests
{
    [Theory]
    [InlineData("01/12/2026")]
    [InlineData("1/12/2026")]
    [InlineData("1/12/26")]
    [InlineData("1-12-26")]
    [InlineData("1.12.2026")]
    [InlineData("011226")]
    [InlineData("01122026")]
    [InlineData(" 1/12/26 ")]
    public void ADayMonthAndYear_IsReadHoweverItIsTyped(string text)
    {
        Assert.Equal(new DateOnly(2026, 12, 1), DateEntry.Parse(text, DateEntryKind.Day));
    }

    [Theory]
    [InlineData("12/26")]
    [InlineData("12/2026")]
    [InlineData("12-26")]
    public void AStart_TypedAsMonthAndYear_IsTheFirst(string text)
    {
        Assert.Equal(new DateOnly(2026, 12, 1), DateEntry.Parse(text, DateEntryKind.Start));
    }

    [Theory]
    [InlineData("11/28", 2028, 11, 30)]
    [InlineData("2/2028", 2028, 2, 29)]
    public void AnEnd_TypedAsMonthAndYear_IsTheLastDay(string text, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), DateEntry.Parse(text, DateEntryKind.End));
    }

    [Theory]
    [InlineData("12/26")]
    [InlineData("")]
    [InlineData("31/02/2026")]
    [InlineData("1/13/26")]
    [InlineData("hola")]
    [InlineData("1/12")]
    public void WhatIsNotADay_IsNoDate(string text)
    {
        Assert.Null(DateEntry.Parse(text, DateEntryKind.Day));
    }

    [Fact]
    public void ADayTypedInFull_StaysThatDay_OnAStartOrAnEnd()
    {
        Assert.Equal(new DateOnly(2026, 12, 15), DateEntry.Parse("15/12/26", DateEntryKind.Start));
        Assert.Equal(new DateOnly(2028, 11, 15), DateEntry.Parse("15/11/28", DateEntryKind.End));
    }

    /// <summary>Four digits on a start or an end are a month and a year (Tomás, 2026-11-17: "si cargas 1226 deberia dejarte").</summary>
    [Fact]
    public void FourDigits_OnAStartOrAnEnd_AreAMonthAndAYear()
    {
        Assert.Equal(new DateOnly(2026, 12, 1), DateEntry.Parse("1226", DateEntryKind.Start));
        Assert.Equal(new DateOnly(2028, 11, 30), DateEntry.Parse("1128", DateEntryKind.End));
        Assert.Null(DateEntry.Parse("1226", DateEntryKind.Day));
    }

    /// <summary>Seven or five digits could be two different days (1122026: 1/12 or 11/2), so they are no date, never a guess.</summary>
    [Theory]
    [InlineData("1122026")]
    [InlineData("11226")]
    public void AnAmbiguousRun_IsNoDate(string text)
    {
        Assert.Null(DateEntry.Parse(text, DateEntryKind.Start));
    }
}
