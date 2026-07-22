using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Parts;

namespace OCAutomatica.Api.Tests.Parts;

public class PartServiceTests
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

    private static PartListResponse Response(params PartDto[] parts)
        => new() { Value = parts.ToList() };

    private static PartDto Part(string partNum, decimal onHand, decimal cost) => new()
    {
        Vendor_VendorID = "001008",
        Vendor_Name = "JARAMILLO TREVINO GERARDO MAGDALENO",
        VendPart_PartNum = partNum,
        Part_PartDescription = "MANGO KG",
        Part_PUM = "KGS",
        PartPlant_MinimumQty = 5m,
        PartPlant_MaximumQty = 50m,
        VendPart_BaseUnitPrice = cost,
        PartPC_ProdCode = "056",
        PartPC1_ProdCode = "",
        Calculated_Inventario = onHand,
        Calculated_CantidadEnTransito = 2m
    };

    [Fact]
    public async Task GetByVendorAsync_MapsEpicorResponse()
    {
        var client = new StubEpicorClient(Response(Part("8310400932", 13.845m, 53m)));
        var service = new PartService(client);

        var parts = await service.GetByVendorAsync("CFSJ_LAF", "LAF", "001008", Creds);

        Assert.Single(parts);
        Assert.Equal("8310400932", parts[0].PartNum);
        Assert.Equal(13.845m, parts[0].OnHandQty);
        Assert.Equal(53m, parts[0].Cost);
    }

    [Fact]
    public async Task GetByVendorAsync_PreservesDecimalPrecision()
    {
        // Guards against the legacy Convert.ToInt32 bug: 0.5 kg must survive
        // the round trip, not be rounded to 0 or 1.
        var client = new StubEpicorClient(Response(Part("X", 0.5m, 12.345m)));
        var service = new PartService(client);

        var parts = await service.GetByVendorAsync("CFSJ_LAF", "LAF", "001008", Creds);

        Assert.Equal(0.5m, parts[0].OnHandQty);
        Assert.Equal(12.345m, parts[0].Cost);
    }

    [Fact]
    public async Task GetByVendorAsync_ReturnsEmptyWhenNoResponse()
    {
        var service = new PartService(new StubEpicorClient(null));

        var parts = await service.GetByVendorAsync("CFSJ_LAF", "LAF", "001008", Creds);

        Assert.Empty(parts);
    }

    [Fact]
    public async Task GetByVendorAsync_PassesPlantAndVendorAsBaqParameters()
    {
        var client = new StubEpicorClient(Response());
        var service = new PartService(client);

        await service.GetByVendorAsync("CFSJ_LAF", "LAF", "001008", Creds);

        // Confirmed by live verification: BaqSvc/{BAQID} alone only returns the
        // resource descriptor ({"name":"Data","kind":"EntitySet",...}) — the
        // actual rows live under the /Data sub-resource. CurrentCompany was
        // tested and found unnecessary (company is already scoped by the URL).
        Assert.Contains("BaqSvc/OCA_PartesPorProveedor/Data", client.LastRelativePath);
        Assert.Contains("CurrentPlant=LAF", client.LastRelativePath);
        Assert.Contains("vendorId=001008", client.LastRelativePath);
    }
}
