using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.PurchaseOrders;
using OCAutomatica.Api.Users;

namespace OCAutomatica.Api.Tests.PurchaseOrders;

public class PurchaseOrderReportServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly Func<string, object?> _byPath;
        public List<string> RequestedPaths { get; } = new();

        public StubEpicorClient(Func<string, object?> byPath) => _byPath = byPath;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
        {
            RequestedPaths.Add(relativePath);
            return Task.FromResult((T?)_byPath(relativePath));
        }

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by PurchaseOrderReportService.");

        public Task<T?> InvokeFunctionAsync<T>(string company, string library, string function,
            object input, EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by PurchaseOrderReportService.");
    }

    private sealed class StubUserDirectoryService : IUserDirectoryService
    {
        private readonly string? _email;
        public StubUserDirectoryService(string? email) => _email = email;
        public Task<string?> GetEmailAsync(string userId, EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult(_email);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static readonly EpicorCredentials Creds = new("epicor", "x");
    private static readonly TimeProvider July2026 =
        new FixedTimeProvider(new DateTimeOffset(2026, 7, 23, 13, 39, 34, TimeSpan.Zero));

    private static PurchaseOrderReportHeaderDto BuildHeader() => new()
    {
        PONum = 3431,
        Approve = true,
        ApprovalStatus = "A",
        BuyerIDName = "Janneth Yañez",
        VendorVendorID = "001008",
        VendorName = "JARAMILLO TREVIÑO GERARDO MAGDALENO",
        OrderDate = new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero),
        ShipViaCode = "LOC",
        CommentText = "CALABAZA GRANDE",
        ShipAddress1 = "",
        ShipAddress2 = "",
        ShipCity = "",
        ShipState = "",
        ShipZIP = "",
        DocTotalTax = 0m,
        TotalWhTax = 0m,
        PODetails = new List<PODetailDto>
        {
            new() { PONUM = 3431, POLine = 1, PartNum = "8323700247", LineDesc = "CALABACITA KG", OrderQty = 4m, IUM = "KGS", UnitCost = 14m },
        }
    };

    private static IEpicorClient BuildClientForFullHappyPath()
    {
        return new StubEpicorClient(path =>
        {
            if (path.Contains("POes")) return BuildHeader();
            if (path.Contains("VendorSvc"))
                return new VendorAddressListResponse
                {
                    Value = new List<VendorAddressDto> { new() { Address1 = "AV LOS ANGELES", Address2 = "1000", City = "SAN NICOLAS DE LOS GARZA" } }
                };
            if (path.Contains("CompanySvc"))
                return new CompanyAddressListResponse
                {
                    Value = new List<CompanyAddressDto>
                    {
                        new() { Address1 = "AVE. ACAPULCO NUM. 1100 Col. RESIDENCIAL SANTA FE", Address2 = "", City = "GUADALUPE", State = "NUEVO LEON", Zip = "67112", StateTaxID = "CFS051213DG5" }
                    }
                };
            if (path.Contains("PlantSvc"))
                return new PlantDetailsListResponse
                {
                    Value = new List<PlantDetailsDto> { new() { Name = "LA FE", PhoneNum = "" } }
                };
            if (path.Contains("ShipViaSvc"))
                return new ShipViaListResponse
                {
                    Value = new List<ShipViaDto> { new() { Description = "LOCAL" } }
                };
            if (path.Contains("PartPCs"))
                return new PartEanListResponse
                {
                    Value = new List<PartEanDto> { new() { PartNum = "8323700247", UOMCode = "KGS", PRODCODE = "12" } }
                };
            throw new InvalidOperationException($"Unexpected path: {path}");
        });
    }

    [Fact]
    public async Task GetReportDataAsync_FallsBackToBlank_WhenASecondaryLookupIsAccessDenied()
    {
        // Confirmed live: a secondary BO (e.g. Erp.BO.ShipVia) can be
        // outside the REST API key's Access Scope even when the user can
        // browse the same catalog fine in the native Kinetic UI. Either
        // way, a gap on one secondary field must not block the whole
        // report - the endpoint used to 503 entirely on this before the fix.
        var client = new StubEpicorClient(path =>
        {
            if (path.Contains("POes")) return BuildHeader();
            if (path.Contains("ShipViaSvc"))
                throw new EpicorException(401, EpicorErrorReason.AccessDenied, "Access denied (Erp.BO.ShipVia.GetRows).");
            if (path.Contains("VendorSvc")) return new VendorAddressListResponse();
            if (path.Contains("CompanySvc")) return new CompanyAddressListResponse();
            if (path.Contains("PlantSvc")) return new PlantDetailsListResponse();
            if (path.Contains("PartPCs")) return new PartEanListResponse();
            throw new InvalidOperationException($"Unexpected path: {path}");
        });
        var service = new PurchaseOrderReportService(client, new StubUserDirectoryService(null), July2026);

        var data = await service.GetReportDataAsync("CFSJ_LAF", "CARNES FINAS SAN JUAN", "LAF", 3431, "epicor", Creds);

        Assert.NotNull(data);
        Assert.Equal(string.Empty, data!.ShipViaDescription);
    }

    [Fact]
    public async Task GetReportDataAsync_BuildsTheFullReportFromAllSources()
    {
        var service = new PurchaseOrderReportService(
            BuildClientForFullHappyPath(), new StubUserDirectoryService("giovanni.montoya@carnessanjuan.com"), July2026);

        var data = await service.GetReportDataAsync(
            "CFSJ_LAF", "CARNES FINAS SAN JUAN", "LAF", 3431, "epicor", Creds);

        Assert.NotNull(data);
        Assert.Equal(3431, data!.PoNum);
        Assert.True(data.Approved);
        Assert.Equal("CARNES FINAS SAN JUAN", data.CompanyName);
        Assert.Equal("LA FE", data.PlantName);
        Assert.Equal("001008", data.VendorId);
        Assert.Equal("AV LOS ANGELES", data.VendorAddress1);
        Assert.Equal("LOCAL", data.ShipViaDescription);
        Assert.Equal("Janneth Yañez", data.BuyerName);
        Assert.Equal("CALABAZA GRANDE", data.CommentText);
        Assert.Equal("giovanni.montoya@carnessanjuan.com", data.BuyerEmail);

        Assert.Single(data.Lines);
        var line = data.Lines[0];
        Assert.Equal(1, line.Line);
        Assert.Equal("8323700247", line.PartNum);
        Assert.Equal("12", line.Ean);
        Assert.Equal(4m, line.OrderQty);
        Assert.Equal(14m, line.UnitCost);
        Assert.Equal(56m, line.ExtendedPrice); // 4 * 14

        Assert.Equal(56m, data.Subtotal);
        Assert.Equal(0m, data.Taxes);
        Assert.Equal(0m, data.Withholdings);
        Assert.Equal(56m, data.Total);
    }

    [Fact]
    public async Task GetReportDataAsync_UsesShipAddress_WhenCaptured()
    {
        // Matches the legacy CASE formula: only falls back to Company's
        // address when POHeader.ShipAddress1 is blank (spec section 2.1).
        var client = new StubEpicorClient(path =>
        {
            if (path.Contains("POes"))
            {
                var header = BuildHeader();
                header.ShipAddress1 = "CALLE FALSA 123";
                header.ShipCity = "MONTERREY";
                header.ShipState = "NUEVO LEON";
                header.ShipZIP = "64000";
                return header;
            }
            if (path.Contains("VendorSvc")) return new VendorAddressListResponse();
            if (path.Contains("CompanySvc")) return new CompanyAddressListResponse();
            if (path.Contains("PlantSvc"))
                return new PlantDetailsListResponse { Value = new List<PlantDetailsDto> { new() { Name = "LA FE", PhoneNum = "8112345678" } } };
            if (path.Contains("ShipViaSvc")) return new ShipViaListResponse();
            if (path.Contains("PartPCs")) return new PartEanListResponse();
            throw new InvalidOperationException($"Unexpected path: {path}");
        });
        var service = new PurchaseOrderReportService(client, new StubUserDirectoryService(null), July2026);

        var data = await service.GetReportDataAsync(
            "CFSJ_LAF", "CARNES FINAS SAN JUAN", "LAF", 3431, "epicor", Creds);

        Assert.Contains("CALLE FALSA 123", data!.DeliveryAddress);
        Assert.Contains("MONTERREY", data.DeliveryAddress);
        Assert.Contains("8112345678", data.DeliveryAddress);
        Assert.DoesNotContain("AVE. ACAPULCO", data.DeliveryAddress);
    }

    [Fact]
    public async Task GetReportDataAsync_FallsBackToCompanyAddress_WhenShipAddressIsBlank()
    {
        var client = BuildClientForFullHappyPath();
        var service = new PurchaseOrderReportService(client, new StubUserDirectoryService(null), July2026);

        var data = await service.GetReportDataAsync(
            "CFSJ_LAF", "CARNES FINAS SAN JUAN", "LAF", 3431, "epicor", Creds);

        Assert.Contains("AVE. ACAPULCO NUM. 1100", data!.DeliveryAddress);
        Assert.Contains("GUADALUPE", data.DeliveryAddress);
        Assert.Contains("CFS051213DG5", data.DeliveryAddress);
    }

    [Fact]
    public async Task GetReportDataAsync_ReturnsNull_WhenPurchaseOrderNotFound()
    {
        var client = new StubEpicorClient(_ => new PurchaseOrderReportHeaderDto { PONum = 0 });
        var service = new PurchaseOrderReportService(client, new StubUserDirectoryService(null), July2026);

        var data = await service.GetReportDataAsync(
            "CFSJ_LAF", "CARNES FINAS SAN JUAN", "LAF", 999999, "epicor", Creds);

        Assert.Null(data);
    }

    [Fact]
    public async Task GetReportDataAsync_LeavesEanBlank_WhenPartHasNoEanCaptured()
    {
        var client = new StubEpicorClient(path =>
        {
            if (path.Contains("POes")) return BuildHeader();
            if (path.Contains("PartPCs")) return new PartEanListResponse(); // no matches
            if (path.Contains("VendorSvc")) return new VendorAddressListResponse();
            if (path.Contains("CompanySvc")) return new CompanyAddressListResponse();
            if (path.Contains("PlantSvc")) return new PlantDetailsListResponse();
            if (path.Contains("ShipViaSvc")) return new ShipViaListResponse();
            throw new InvalidOperationException($"Unexpected path: {path}");
        });
        var service = new PurchaseOrderReportService(client, new StubUserDirectoryService(null), July2026);

        var data = await service.GetReportDataAsync(
            "CFSJ_LAF", "CARNES FINAS SAN JUAN", "LAF", 3431, "epicor", Creds);

        Assert.Equal(string.Empty, data!.Lines[0].Ean);
    }
}
