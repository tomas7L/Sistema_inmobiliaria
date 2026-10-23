namespace Inmobiliaria.Presentation.Voiding;

/// <summary>The voiding screen's fixed operator texts.</summary>
public static class VoidingTexts
{
    public const string Done = "Cobro anulado.";

    /// <summary>The voiding was recorded but the pair could not be read back to show it annulled.</summary>
    public const string DoneButNotShown =
        "Cobro anulado. No se pudo mostrar el recibo actualizado: búsquelo otra vez para verlo anulado. No vuelva a anularlo.";
}
