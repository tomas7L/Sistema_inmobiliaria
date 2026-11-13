namespace Inmobiliaria.Presentation.Notices;

/// <summary>
/// What the Avisos screen says. Placeholder wording: the final text lands with Mateo's screens (notificaciones design D10,
/// tasks 4.2), as Mora's does.
/// </summary>
public static class NoticesTexts
{
    public const string Empty = "No hay avisos para enviar hoy.";

    public const string NoMailbox =
        "No se puede enviar: la casilla de Gmail no está configurada en esta PC. La configura un usuario Admin.";

    public const string NoAgencyName = "No se puede enviar: falta configurar Agency:Name en appsettings.json.";

    public const string MailboxUnreadable =
        "No se puede enviar: Windows no pudo leer la casilla de Gmail guardada en esta PC. Un usuario Admin puede volver a guardarla.";

    public const string MailboxNotStored = "La casilla de Gmail no está configurada en esta PC.";

    public static string MailboxStored(string address) => $"Los avisos se envían desde {address}.";

    /// <summary>The counts spec Requirement 5 asks for after a press.</summary>
    public static string Report(int sent, int alreadySent, int failed) =>
        $"Enviados: {sent}. Ya enviados antes: {alreadySent}. Con error: {failed}.";

    public static string Failure(string shortId, string tenant, string reason) => $"{shortId} ({tenant}): {reason}";

    /// <summary>Rows chosen for a press that no longer owed anything when it ran, so nothing was sent to them.</summary>
    public static string NoLongerOwed(IEnumerable<string> rows) =>
        "No se enviaron porque al enviar ya no figuraban con deuda: " + string.Join(", ", rows) + ".";

    public static string Row(string shortId, string tenant) => $"{shortId} ({tenant})";

    /// <summary>A press that failed as a whole. What Gmail accepted before the failure is recorded and shows as already sent.</summary>
    public const string SendFailed =
        "No se pudo completar el envío. Actualizá la lista: los avisos que salieron figuran como ya enviados.";

    public const string SaveFailed = "No se pudo guardar la casilla de Gmail en esta PC.";
}
