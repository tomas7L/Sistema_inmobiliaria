using System.Windows;
using System.Windows.Input;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// What one list of people on the contract form shares with its rows: which field its errors belong to, and how a row is
/// removed. A Freezable, so its bindings reach the screen's view-model from an ItemsControl's Tag.
/// </summary>
public sealed class RowListContext : Freezable
{
    public static readonly DependencyProperty FieldProperty =
        DependencyProperty.Register(nameof(Field), typeof(string), typeof(RowListContext));

    public static readonly DependencyProperty RemoveCommandProperty =
        DependencyProperty.Register(nameof(RemoveCommand), typeof(ICommand), typeof(RowListContext));

    /// <summary>The <c>ContractField</c> the rows' errors belong to, by name.</summary>
    public string? Field
    {
        get => (string?)GetValue(FieldProperty);
        set => SetValue(FieldProperty, value);
    }

    /// <summary>"Quitar", given the row.</summary>
    public ICommand? RemoveCommand
    {
        get => (ICommand?)GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new RowListContext();
}
