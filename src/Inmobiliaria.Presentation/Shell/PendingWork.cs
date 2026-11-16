using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Shell;

/// <summary>
/// What a screen would lose if the session closed now (12.3d, Tomás 2026-11-16): an operation still running, or a
/// cobro or anulación shown and not confirmed. Cerrar sesión asks first when any screen has some.
/// </summary>
public interface IPendingWork
{
    /// <summary>One sentence naming the work, or null when there is none.</summary>
    string? PendingWork { get; }
}

/// <summary>The texts Cerrar sesión shows.</summary>
public static class PendingWorkTexts
{
    public const string OperationRunning = "Hay una operación en curso.";

    public const string CobroNotConfirmed = "Hay un cobro sin confirmar.";

    public const string AnulacionNotConfirmed = "Hay una anulación sin confirmar.";

    public const string Question = "¿Cerrar sesión igual? Lo que no se confirmó se pierde.";
}

/// <summary>A screen whose only pending work is an operation its runner is still running.</summary>
public sealed class RunningWork(OperationRunner runner) : IPendingWork
{
    public string? PendingWork => runner.IsBusy ? PendingWorkTexts.OperationRunning : null;
}
