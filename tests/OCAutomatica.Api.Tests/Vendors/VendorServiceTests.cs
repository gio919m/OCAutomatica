using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Vendors;

namespace OCAutomatica.Api.Tests.Vendors;

public class VendorServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly object? _response;
        public string? LastRelativePath { get; private set; }

        public StubEpicorClient(object? response) => _response = response;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
        {
            LastRelativePath = relativePath;
            return Task.FromResult((T?)_response);
        }

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_response);

        public Task<T?> InvokeFunctionAsync<T>(
            string company, string library, string function, object input,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException(
                "InvokeFunctionAsync is not exercised by this test suite.");
    }

    private static readonly EpicorCredentials Creds = new("jyanez", "x");

    private static VendorListResponse Response(params (string Id, string Name)[] vendors)
        => new()
        {
            Value = vendors
                .Select(v => new VendorDto { VendorID = v.Id, Name = v.Name })
                .ToList()
        };

    [Fact]
    public async Task SearchAsync_MapsEpicorResponse()
    {
        var client = new StubEpicorClient(Response(
            ("001008", "JARAMILLO TREVINO GERARDO MAGDALENO"),
            ("002801", "ALANIS VILLARREAL JULIO CESAR")));
        var service = new VendorService(client);

        var vendors = await service.SearchAsync("CFSJ_LAF", "JARAMILLO", Creds);

        Assert.Equal(2, vendors.Count);
        Assert.Equal("001008", vendors[0].VendorId);
        Assert.Equal("JARAMILLO TREVINO GERARDO MAGDALENO", vendors[0].Name);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyWhenNoResponse()
    {
        var service = new VendorService(new StubEpicorClient(null));

        var vendors = await service.SearchAsync("CFSJ_LAF", "nada", Creds);

        Assert.Empty(vendors);
    }

    [Fact]
    public async Task SearchAsync_EscapesSearchTermInFilter()
    {
        // Prevents a vendor name containing a single quote from breaking the
        // OData $filter (same class of bug as the legacy sp_EnviaOC_V2 SQL
        // injection — but here it's an OData filter, so the fix is escaping
        // the embedded quote by doubling it, OData's own escape convention).
        // The doubled quote is then percent-encoded (Uri.EscapeDataString
        // escapes ' to %27 on this runtime), which is transparent to Epicor
        // once the server URL-decodes the query string, so the doubled
        // quote still reaches the OData parser as "''".
        var client = new StubEpicorClient(Response());
        var service = new VendorService(client);

        await service.SearchAsync("CFSJ_LAF", "O'BRIEN", Creds);

        Assert.Contains("O%27%27BRIEN", client.LastRelativePath);
    }

    [Fact]
    public async Task SearchAsync_PercentEncodesAmpersandInSearchTerm()
    {
        // A raw '&' in the search term would be parsed as a new query
        // parameter by the OData endpoint, silently breaking the $top=20
        // cap (or worse). It must be percent-encoded before it reaches the
        // query string.
        var client = new StubEpicorClient(Response());
        var service = new VendorService(client);

        await service.SearchAsync("CFSJ_LAF", "SMITH & SONS", Creds);

        Assert.Contains("%26", client.LastRelativePath);
        Assert.DoesNotContain("SMITH & SONS", client.LastRelativePath);
    }

    [Fact]
    public async Task SearchAsync_LimitsResultsToTwenty()
    {
        var client = new StubEpicorClient(Response());
        var service = new VendorService(client);

        await service.SearchAsync("CFSJ_LAF", "a", Creds);

        Assert.Contains("$top=20", client.LastRelativePath);
    }
}
