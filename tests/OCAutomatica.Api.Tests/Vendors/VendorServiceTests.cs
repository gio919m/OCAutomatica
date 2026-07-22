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
        // the embedded quote by doubling it, OData's own escape convention.
        var client = new StubEpicorClient(Response());
        var service = new VendorService(client);

        await service.SearchAsync("CFSJ_LAF", "O'BRIEN", Creds);

        Assert.Contains("O''BRIEN", client.LastRelativePath);
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
