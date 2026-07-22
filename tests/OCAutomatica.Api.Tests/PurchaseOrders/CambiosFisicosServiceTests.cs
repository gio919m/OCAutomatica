using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.PurchaseOrders;

namespace OCAutomatica.Api.Tests.PurchaseOrders;

public class CambiosFisicosServiceTests
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

    private static CambioFisicoListResponse Response(params CambioFisicoDto[] rows)
        => new() { Value = rows.ToList() };

    private static CambioFisicoDto Row(
        string character01, string character02, string character04,
        decimal cantidadPendiente, string character06) => new()
    {
        UD104A_Character01 = character01,
        UD104A_Character02 = character02,
        UD104A_Character04 = character04,
        Calculated_CantidadPendiente = cantidadPendiente,
        UD104A_Character06 = character06
    };

    [Fact]
    public async Task GetPendingAsync_MapsEpicorResponse()
    {
        var client = new StubEpicorClient(Response(
            Row("8310400932", "MANGO KG", "KGS", 13.845m, "056")));
        var service = new CambiosFisicosService(client);

        var rows = await service.GetPendingAsync("CFSJ_LAF", "LAF", "001008", Creds);

        Assert.Single(rows);
        Assert.Equal("8310400932", rows[0].Character01);
        Assert.Equal("MANGO KG", rows[0].Character02);
        Assert.Equal("KGS", rows[0].Character04);
        Assert.Equal(13.845m, rows[0].CantidadPendiente);
        Assert.Equal("056", rows[0].Character06);
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsEmptyWhenNoResponse()
    {
        var service = new CambiosFisicosService(new StubEpicorClient(null));

        var rows = await service.GetPendingAsync("CFSJ_LAF", "LAF", "001008", Creds);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetPendingAsync_PassesPlantAndVendorAsBaqParameters()
    {
        var client = new StubEpicorClient(Response());
        var service = new CambiosFisicosService(client);

        await service.GetPendingAsync("CFSJ_LAF", "LAF", "001008", Creds);

        // Confirmed by live verification: CurrentPlant/vendorId are the real
        // BAQ parameter names; CurrentCompany is not sent (same pattern as
        // OCA_PartesPorProveedor in Plan 2).
        Assert.Contains("BaqSvc/OCA_CambiosFisicos/Data", client.LastRelativePath);
        Assert.Contains("CurrentPlant=LAF", client.LastRelativePath);
        Assert.Contains("vendorId=001008", client.LastRelativePath);
    }
}
