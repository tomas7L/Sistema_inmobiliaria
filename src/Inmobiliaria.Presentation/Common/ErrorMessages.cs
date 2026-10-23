using Inmobiliaria.Presentation.Shell;

namespace Inmobiliaria.Presentation.Common;

/// <summary>
/// The fixed operator text for a failure that is not a refusal (design D24). Fixed on purpose: nothing
/// from the exception, the connection or the database can reach the screen through it.
/// </summary>
public static class ErrorMessages
{
    /// <summary>Shown for any exception from a screen operation. The typed values are still on the screen.</summary>
    public const string Generic =
        "No se pudo completar la operación. Los datos escritos se conservaron. Revise la conexión e intente de nuevo.";

    /// <summary>
    /// Shown when a commit raised: a connection lost after the commit was sent can have written it, so the
    /// outcome is unknown and the operator is told where to check.
    /// </summary>
    public static readonly string CommitFailed =
        Generic +
        " Atención: puede que el cobro se haya registrado o no. " +
        $"Antes de volver a intentarlo, verifique en \"{NavigationTitles.RecentCollections}\" si quedó registrado.";

    /// <summary>Shown when a voiding raised: as with a commit, it may or may not have been applied.</summary>
    public static readonly string VoidFailed =
        Generic +
        " Atención: puede que la anulación se haya registrado o no. " +
        "Antes de volver a intentarlo, busque el recibo otra vez y verifique si figura anulado.";
}
