namespace Inmobiliaria.Presentation.Login;

/// <summary>
/// What the login window says. The first three are the texts the window always showed; the wait texts are Mateo's,
/// approved by Tomás on 2026-11-13 (limite-de-intentos-de-login, design §5). The length of the wait is written from
/// <see cref="LoginGate.Wait"/>, so changing that constant changes the texts too.
/// </summary>
public static class LoginTexts
{
    public const string Rejected = "Usuario o contraseña incorrectos.";

    public const string NotProvisioned = "Esta cuenta no puede iniciar sesión. Consulte al administrador.";

    public const string NoVerdict =
        "No se pudo conectar con el sistema. Revise la conexión a internet e intente de nuevo. Si sigue igual, avise al administrador.";

    public const string WaitTitle = "Demasiados intentos fallidos";

    public static readonly string TooManyFailures =
        $"Hubo demasiados intentos fallidos. Espere {Clock(LoginGate.Wait)} antes de volver a intentar. Si intenta antes, la espera se alarga.";

    public static readonly string Blocked =
        $"Hubo demasiados intentos fallidos y el ingreso quedó en pausa. Espere {Clock(LoginGate.Wait)} antes de volver a intentar. Si intenta antes, la espera se alarga.";

    /// <summary>The button's text during a wait, for example "Espere 2:30" or "Espere 0:01".</summary>
    public static string Countdown(TimeSpan left) => $"Espere {Clock(left)}";

    /// <summary>Minutes and seconds, rounding a part second up, so the last second still reads 0:01.</summary>
    private static string Clock(TimeSpan time)
    {
        var seconds = (int)Math.Ceiling(Math.Max(0, time.TotalSeconds));
        return $"{seconds / 60}:{seconds % 60:00}";
    }
}
