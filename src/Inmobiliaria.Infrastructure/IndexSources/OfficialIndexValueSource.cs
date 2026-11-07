using System.Text.Json;
using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Infrastructure.IndexSources;

/// <summary>
/// <see cref="IIndexValueSource"/> over the two public APIs (gestion-de-alquileres spec Requirement 25, design §5). The request
/// carries no client data and no credentials. Nothing but the caller's own cancellation leaves as an exception, and invalid
/// settings read as a failing source, since a wrong address must not stop anything but the button.
/// </summary>
public sealed class OfficialIndexValueSource : IIndexValueSource
{
    private const string Icl = "ICL";
    private const string Ipc = "IPC";

    private static readonly IndexFetch BadResponse = new IndexFetch.SourceFailed(ServerError: false);

    private readonly HttpClient _http;
    private readonly IndexSourceSettings _settings;

    public OfficialIndexValueSource(HttpClient http, IndexSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(settings);

        _http = http;
        _settings = settings;
    }

    /// <summary>The one client the application uses, with the timeout of design D22.</summary>
    public static HttpClient SharedClient { get; } = new() { Timeout = TimeSpan.FromSeconds(10) };

    public bool Supports(string indexName) => indexName is Icl or Ipc;

    public Task<IndexFetch> FetchAsync(string indexName, IndexPeriod period, CancellationToken ct = default) => indexName switch
    {
        Icl when _settings.Bcra is { } bcra => AskAsync(IclResponse.Address(bcra, period), root => IclResponse.Read(root, period), ct),
        Ipc when _settings.DatosGob is { } datosGob => AskAsync(IpcResponse.Address(datosGob, period), root => IpcResponse.Read(root, period), ct),
        _ => Task.FromResult(BadResponse),
    };

    private async Task<IndexFetch> AskAsync(string address, Func<JsonElement, IndexFetch> read, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(address, ct);
            if (!response.IsSuccessStatusCode)
            {
                return new IndexFetch.SourceFailed(ServerError: true);
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            return read(document.RootElement);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new IndexFetch.Unreachable(TimedOut: true);
        }
        catch (HttpRequestException)
        {
            return new IndexFetch.Unreachable(TimedOut: false);
        }
        catch (JsonException)
        {
            return BadResponse;
        }
        catch (InvalidOperationException)
        {
            // What reading the body throws when the response declares a charset that does not exist.
            return BadResponse;
        }
    }
}
