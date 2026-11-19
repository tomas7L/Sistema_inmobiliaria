using System.Windows;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// Marks an input whose field has an error, as the canvas's ".inp-err" (a 2 px border in the "bad" colour): bind
/// <c>Text</c> to the field's own error and, when one error covers several fields, <c>Shared</c> to that one, as "Cargue
/// un teléfono o un email". <c>Theme.Input</c> paints the border while <c>HasError</c> is true.
/// </summary>
public static class FieldError
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(FieldError), new PropertyMetadata(null, Recompute));

    public static readonly DependencyProperty SharedProperty = DependencyProperty.RegisterAttached(
        "Shared", typeof(string), typeof(FieldError), new PropertyMetadata(null, Recompute));

    private static readonly DependencyPropertyKey HasErrorKey = DependencyProperty.RegisterAttachedReadOnly(
        "HasError", typeof(bool), typeof(FieldError), new PropertyMetadata(false));

    public static readonly DependencyProperty HasErrorProperty = HasErrorKey.DependencyProperty;

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    public static string? GetShared(DependencyObject element) => (string?)element.GetValue(SharedProperty);

    public static void SetShared(DependencyObject element, string? value) => element.SetValue(SharedProperty, value);

    public static bool GetHasError(DependencyObject element) => (bool)element.GetValue(HasErrorProperty);

    private static void Recompute(DependencyObject element, DependencyPropertyChangedEventArgs _) =>
        element.SetValue(HasErrorKey, !string.IsNullOrEmpty(GetText(element)) || !string.IsNullOrEmpty(GetShared(element)));
}
