using OCAutomatica.Api.Buyers;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Tests.Buyers;

public class BuyerServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly object? _response;

        public StubEpicorClient(object? response) => _response = response;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_response);

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_response);

        public Task<T?> InvokeFunctionAsync<T>(
            string company, string library, string function, object input,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException(
                "InvokeFunctionAsync is not exercised by this test suite.");
    }

    private static BuyerListResponse Response(params BuyerDto[] buyers)
        => new() { Value = buyers.ToList() };

    private static BuyerDto Buyer(string id, string name, params (string User, bool IsDefault)[] auth)
        => new()
        {
            BuyerID = id,
            Name = name,
            PurAuths = auth
                .Select(a => new PurAuthDto { DcdUserID = a.User, IsPrimaryUser = a.IsDefault })
                .ToList()
        };

    private static readonly EpicorCredentials Creds = new("jyanez", "x");

    [Fact]
    public async Task ResolveDefaultBuyerAsync_ReturnsBuyerWhereUserIsDefault()
    {
        var client = new StubEpicorClient(Response(
            Buyer("LAF-CM5", "ESTEFANY JUAREZ", ("nestefany", true), ("jyanez", false)),
            Buyer("LAF-CM9", "JANNETH YANEZ", ("jyanez", true))));
        var service = new BuyerService(client);

        var buyer = await service.ResolveDefaultBuyerAsync("CFSJ_LAF", "jyanez", Creds);

        Assert.NotNull(buyer);
        Assert.Equal("LAF-CM9", buyer!.BuyerId);
        Assert.Equal("JANNETH YANEZ", buyer.Name);
    }

    [Fact]
    public async Task ResolveDefaultBuyerAsync_ReturnsNullWhenUserIsOnlyAuthorized()
    {
        // jyanez can edit LAF-CM5's orders but is not its Default Buyer,
        // so they cannot create orders on that buyer's behalf.
        var client = new StubEpicorClient(Response(
            Buyer("LAF-CM5", "ESTEFANY JUAREZ", ("nestefany", true), ("jyanez", false))));
        var service = new BuyerService(client);

        var buyer = await service.ResolveDefaultBuyerAsync("CFSJ_LAF", "jyanez", Creds);

        Assert.Null(buyer);
    }

    [Fact]
    public async Task ResolveDefaultBuyerAsync_ReturnsNullWhenUserHasNoBuyer()
    {
        var client = new StubEpicorClient(Response(
            Buyer("LAF-CM5", "ESTEFANY JUAREZ", ("nestefany", true))));
        var service = new BuyerService(client);

        var buyer = await service.ResolveDefaultBuyerAsync("CFSJ_LAF", "otro", Creds);

        Assert.Null(buyer);
    }

    [Fact]
    public async Task ResolveDefaultBuyerAsync_IgnoresUsernameCasing()
    {
        var client = new StubEpicorClient(Response(
            Buyer("LAF-CM9", "JANNETH YANEZ", ("JYanez", true))));
        var service = new BuyerService(client);

        var buyer = await service.ResolveDefaultBuyerAsync("CFSJ_LAF", "jyanez", Creds);

        Assert.NotNull(buyer);
        Assert.Equal("LAF-CM9", buyer!.BuyerId);
    }

    [Fact]
    public async Task ResolveDefaultBuyerAsync_ReturnsNullWhenNoBuyersExist()
    {
        var client = new StubEpicorClient(Response());
        var service = new BuyerService(client);

        var buyer = await service.ResolveDefaultBuyerAsync("CFSJ_LAF", "jyanez", Creds);

        Assert.Null(buyer);
    }
}
