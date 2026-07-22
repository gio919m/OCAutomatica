using System.Text.Json;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.PurchaseOrders;

namespace OCAutomatica.Api.Tests.PurchaseOrders;

public class PurchaseOrderServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly object? _response;
        public string? LastCompany { get; private set; }
        public string? LastLibrary { get; private set; }
        public string? LastFunction { get; private set; }
        public object? LastInput { get; private set; }

        public StubEpicorClient(object? response) => _response = response;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by PurchaseOrderService.");

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by PurchaseOrderService.");

        public Task<T?> InvokeFunctionAsync<T>(string company, string library, string function,
            object input, EpicorCredentials credentials, CancellationToken ct = default)
        {
            LastCompany = company;
            LastLibrary = library;
            LastFunction = function;
            LastInput = input;

            // Round-trip through JSON, matching how the real EpicorClient turns an
            // HTTP response body into T (deserialize, never a raw C# cast) - this lets
            // the stub be configured with a plain anonymous object, same as the real
            // Function's JSON response shape.
            if (_response is null) return Task.FromResult(default(T));
            var json = JsonSerializer.Serialize(_response);
            return Task.FromResult(JsonSerializer.Deserialize<T>(json));
        }
    }

    private static readonly EpicorCredentials Creds = new("jyanez", "x");

    private static CreatePurchaseOrderRequest Request(params PurchaseOrderLine[] lines) =>
        new("001008", "prueba", lines.ToList());

    [Fact]
    public async Task CreateAsync_InvokesTheOCACrearOCFunction()
    {
        var client = new StubEpicorClient(new { PONum = 123456 });
        var service = new PurchaseOrderService(client);

        var result = await service.CreateAsync(
            "CFSJ_LAF", "LAF", "LAF-CM5",
            Request(new PurchaseOrderLine("8310400932", 5.5m, 53m, "KGS")),
            Creds);

        Assert.Equal(123456, result.PoNum);
        Assert.Equal("CFSJ_LAF", client.LastCompany);
        Assert.Equal("OCACrearOC", client.LastLibrary);
        Assert.Equal("OCACrearOC", client.LastFunction);
    }

    [Fact]
    public async Task CreateAsync_PreservesDecimalPrecisionInLines()
    {
        // Guards the same class of bug fixed in Plan 2's PartRow: quantities
        // and costs must survive as decimal, never truncated en route to the
        // Function's Lineas input.
        var client = new StubEpicorClient(new { PONum = 1 });
        var service = new PurchaseOrderService(client);

        await service.CreateAsync(
            "CFSJ_LAF", "LAF", "LAF-CM5",
            Request(new PurchaseOrderLine("X", 0.5m, 12.345m, "KGS")),
            Creds);

        var input = Assert.IsType<PurchaseOrderFunctionInput>(client.LastInput);
        var lineas = JsonSerializer.Deserialize<List<PurchaseOrderLineInput>>(input.Lineas)!;
        Assert.Equal(0.5m, lineas[0].Cantidad);
        Assert.Equal(12.345m, lineas[0].Costo);
    }

    [Fact]
    public async Task CreateAsync_SendsBuyerIdAndPlantAsReceived()
    {
        var client = new StubEpicorClient(new { PONum = 1 });
        var service = new PurchaseOrderService(client);

        await service.CreateAsync(
            "CFSJ_LAF", "LAF", "LAF-CM5",
            Request(new PurchaseOrderLine("X", 1m, 1m, "KGS")),
            Creds);

        var input = Assert.IsType<PurchaseOrderFunctionInput>(client.LastInput);
        Assert.Equal("LAF", input.Plant);
        Assert.Equal("LAF-CM5", input.BuyerID);
        Assert.Equal("001008", input.VendorID);
        Assert.Equal("prueba", input.Comentarios);
    }
}
