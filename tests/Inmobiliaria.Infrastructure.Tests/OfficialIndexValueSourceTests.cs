using System.Net;
using System.Text.Json;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Infrastructure.IndexSources;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirement 25, Tests 82 and 84 to 86, on the adapter. The responses replay what the live sources
/// answered on 2026-11-07. A fake handler stands in for the network, so nothing here touches it and no container is needed.
/// </summary>
public class OfficialIndexValueSourceTests
{
    private const string IclJuly = """{"status":200,"metadata":{"resultset":{"count":1,"offset":0,"limit":1000}},"results":[{"idVariable":40,"detalle":[{"fecha":"2026-07-01","valor":34.24000000000}]}]}""";
    private const string IclEmpty = """{"status":200,"metadata":{"resultset":{"count":0,"offset":0,"limit":1000}},"results":[{"idVariable":40,"detalle":[]}]}""";
    private const string IpcAugust = """{"data": [["2026-08-01", 12276.766]], "count": 1, "meta": [{"frequency": "month"}]}""";
    private const string IpcEmpty = """{"data": [], "count": 0, "meta": [{"frequency": "month"}]}""";

    private static readonly IndexPeriod July = new(2026, 7);
    private static readonly IndexPeriod August = new(2026, 8);

    private sealed class ReplayHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);
            return respond(request, cancellationToken);
        }
    }

    private static ReplayHandler Answering(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));

    private static IndexSourceSettings Settings()
    {
        using var document = JsonDocument.Parse("""{ "IndexSources": { "Bcra": "https://bcra.test/v4", "DatosGob": "https://datos.test/api" } }""");
        return IndexSourceSettings.Read(document.RootElement);
    }

    private static OfficialIndexValueSource Source(HttpMessageHandler handler) => new(new HttpClient(handler), Settings());

    // ---- Which index has a source ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("ICL", true)]
    [InlineData("IPC", true)]
    [InlineData("RIPTE", false)]
    public void OnlyIclAndIpc_HaveASource(string index, bool expected) =>
        Assert.Equal(expected, Source(Answering(IclJuly)).Supports(index));

    [Fact]
    public async Task AnIndexWithNoSource_RequestsNothing_AndFailsWithoutThrowing()
    {
        var handler = Answering(IclJuly);

        var fetch = await Source(handler).FetchAsync("RIPTE", July);

        Assert.Equal(new IndexFetch.SourceFailed(ServerError: false), fetch);
        Assert.Empty(handler.Asked);
    }

    // ---- Test 82: the exact request, and the value of the 1st ------------------------------------------------------------

    [Fact]
    public async Task Icl_AsksForThe1stOfTheMonth_AndReadsItsValue()
    {
        var handler = Answering(IclJuly);

        var fetch = await Source(handler).FetchAsync("ICL", July);

        Assert.Equal(new IndexFetch.Found(34.24m), fetch);
        Assert.Equal("https://bcra.test/v4/monetarias/40?desde=2026-07-01&hasta=2026-07-01", Assert.Single(handler.Asked).AbsoluteUri);
    }

    [Fact]
    public async Task Ipc_AsksForThatMonthOnItsSeries_AndReadsItsValue()
    {
        var handler = Answering(IpcAugust);

        var fetch = await Source(handler).FetchAsync("IPC", August);

        Assert.Equal(new IndexFetch.Found(12276.766m), fetch);
        Assert.Equal(
            "https://datos.test/api/series/?ids=148.3_INIVELNAL_DICI_M_26&start_date=2026-08-01&limit=1&format=json",
            Assert.Single(handler.Asked).AbsoluteUri);
    }

    [Fact]
    public async Task ALevelIsReadAsADecimal_NeverThroughFloatingPoint()
    {
        var fetch = await Source(Answering("""{"data": [["2026-08-01", 1915878.760001]]}""")).FetchAsync("IPC", August);

        Assert.Equal(new IndexFetch.Found(1915878.760001m), fetch);
    }

    // ---- Test 85: a month not there yet ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("ICL", IclEmpty)]
    [InlineData("IPC", IpcEmpty)]
    [InlineData("IPC", """{"data": [["2026-09-01", 12400.5]]}""")]
    public async Task AMonthTheSourceDoesNotHold_IsNotPublishedYet(string index, string body)
    {
        var fetch = await Source(Answering(body)).FetchAsync(index, index == "ICL" ? July : August);

        Assert.IsType<IndexFetch.NotPublishedYet>(fetch);
    }

    // ---- Test 86: a figure is not trusted beyond its format --------------------------------------------------------------

    [Theory]
    [InlineData("ICL", """{"results":[{"detalle":[{"fecha":"2026-07-02","valor":34.31}]}]}""")]
    [InlineData("ICL", """{"results":[{"detalle":[{"fecha":"2026-07-01","valor":0}]}]}""")]
    [InlineData("ICL", """{"results":[{"detalle":[{"fecha":"2026-07-01","valor":-34.24}]}]}""")]
    [InlineData("ICL", """{"results":[{"detalle":[{"fecha":"2026-07-01","valor":34.24},{"fecha":"2026-07-02","valor":34.31}]}]}""")]
    [InlineData("IPC", """{"data": [["2026-07-01", 12000.5]]}""")]
    [InlineData("IPC", """{"data": [["2026-08-01", 0]]}""")]
    [InlineData("IPC", """{"data": [["2026-08-01", -5]]}""")]
    public async Task TheWrongMonthOrANonPositiveLevel_IsASourceFailure(string index, string body)
    {
        var fetch = await Source(Answering(body)).FetchAsync(index, index == "ICL" ? July : August);

        Assert.Equal(new IndexFetch.SourceFailed(ServerError: false), fetch);
    }

    // ---- Test 84: each failure, none throwing ----------------------------------------------------------------------------

    [Theory]
    [InlineData("ICL", "<html>not json</html>")]
    [InlineData("ICL", "[]")]
    [InlineData("ICL", "{}")]
    [InlineData("ICL", """{"results":[]}""")]
    [InlineData("ICL", """{"results":[{}]}""")]
    [InlineData("ICL", """{"results":[{"detalle":[{"valor":34.24}]}]}""")]
    [InlineData("ICL", """{"results":[{"detalle":[{"fecha":"2026-07-01"}]}]}""")]
    [InlineData("ICL", """{"results":[{"detalle":[{"fecha":"2026-07-01","valor":"34.24"}]}]}""")]
    [InlineData("ICL", """{"results":[{"detalle":[{"fecha":"julio","valor":34.24}]}]}""")]
    [InlineData("ICL", """{"results":[{"detalle":[{"fecha":"2026-07-01","valor":1e300}]}]}""")]
    [InlineData("IPC", "<html>not json</html>")]
    [InlineData("IPC", "[]")]
    [InlineData("IPC", "{}")]
    [InlineData("IPC", """{"data": "none"}""")]
    [InlineData("IPC", """{"data": ["2026-08-01"]}""")]
    [InlineData("IPC", """{"data": [["2026-08-01"]]}""")]
    [InlineData("IPC", """{"data": [["2026-08-01", null]]}""")]
    [InlineData("IPC", """{"data": [["2026-08-01", "12276.766"]]}""")]
    [InlineData("IPC", """{"data": [[20260801, 12276.766]]}""")]
    [InlineData("IPC", """{"data": [["agosto", 12276.766]]}""")]
    public async Task AChangedFormat_IsASourceFailure_NotAServerError(string index, string body)
    {
        var fetch = await Source(Answering(body)).FetchAsync(index, index == "ICL" ? July : August);

        Assert.Equal(new IndexFetch.SourceFailed(ServerError: false), fetch);
    }

    [Theory]
    [InlineData("ICL", HttpStatusCode.InternalServerError)]
    [InlineData("IPC", HttpStatusCode.BadGateway)]
    [InlineData("ICL", HttpStatusCode.BadRequest)]
    [InlineData("IPC", HttpStatusCode.NotFound)]
    public async Task AnErrorStatus_IsAServerError(string index, HttpStatusCode status)
    {
        var fetch = await Source(Answering("""{"status":400,"errorMessages":["Parámetro erróneo"]}""", status)).FetchAsync(index, July);

        Assert.Equal(new IndexFetch.SourceFailed(ServerError: true), fetch);
    }

    [Fact]
    public async Task ACharsetThatDoesNotExist_IsASourceFailure_AndDoesNotThrow()
    {
        var handler = new ReplayHandler((_, _) =>
        {
            var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(IclJuly));
            content.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=no-such-charset");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        var fetch = await Source(handler).FetchAsync("ICL", July);

        Assert.Equal(new IndexFetch.SourceFailed(ServerError: false), fetch);
    }

    [Theory]
    [InlineData("ICL")]
    [InlineData("IPC")]
    public async Task NoConnection_IsUnreachable(string index)
    {
        var handler = new ReplayHandler((_, _) => throw new HttpRequestException("No such host is known."));

        var fetch = await Source(handler).FetchAsync(index, July);

        Assert.Equal(new IndexFetch.Unreachable(TimedOut: false), fetch);
    }

    [Fact]
    public async Task ASlowSource_TimesOutThroughTheClient_AndIsUnreachable()
    {
        var handler = new ReplayHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Unreachable: the delay only ends by cancelling.");
        });
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };

        var fetch = await new OfficialIndexValueSource(client, Settings()).FetchAsync("ICL", July);

        Assert.Equal(new IndexFetch.Unreachable(TimedOut: true), fetch);
    }

    [Fact]
    public async Task TheCallerCancelling_IsNotReportedAsATimeout()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var handler = new ReplayHandler((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source(handler).FetchAsync("ICL", July, cancel.Token));
    }

    [Fact]
    public async Task InvalidSettings_RequestNothing_AndFailWithoutThrowing()
    {
        using var document = JsonDocument.Parse("{}");
        var handler = Answering(IclJuly);
        var source = new OfficialIndexValueSource(new HttpClient(handler), IndexSourceSettings.Read(document.RootElement));

        var fetch = await source.FetchAsync("ICL", July);

        Assert.Equal(new IndexFetch.SourceFailed(ServerError: false), fetch);
        Assert.Empty(handler.Asked);
    }

    // ---- The request carries nothing of the agency's -----------------------------------------------------------------------

    [Fact]
    public async Task TheRequest_CarriesNoHeaderOfTheAgencys_AndNoBody()
    {
        HttpRequestMessage? seen = null;
        var handler = new ReplayHandler((request, _) =>
        {
            seen = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(IclJuly) });
        });

        await Source(handler).FetchAsync("ICL", July);

        Assert.Equal(HttpMethod.Get, seen!.Method);
        Assert.Null(seen.Content);
        Assert.Empty(seen.Headers);
    }

    [Fact]
    public void TheClient_TimesOutAtTenSeconds() =>
        Assert.Equal(TimeSpan.FromSeconds(10), OfficialIndexValueSource.SharedClient.Timeout);
}
