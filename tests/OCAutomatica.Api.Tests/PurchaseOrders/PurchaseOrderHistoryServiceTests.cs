using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.PurchaseOrders;

namespace OCAutomatica.Api.Tests.PurchaseOrders;

public class PurchaseOrderHistoryServiceTests
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
            => throw new NotSupportedException("Not used by PurchaseOrderHistoryService.");

        public Task<T?> InvokeFunctionAsync<T>(string company, string library, string function,
            object input, EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by PurchaseOrderHistoryService.");
    }

    private static readonly EpicorCredentials Creds = new("jyanez", "x");

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly TimeProvider July2026 =
        new FixedTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GetByVendorAsync_ResolvesVendorNumThenFiltersPOesByIt()
    {
        var client = new StubEpicorClient(path =>
        {
            if (path.Contains("VendorSvc"))
                return new VendorNumListResponse { Value = new List<VendorNumDto> { new() { VendorNum = 1323 } } };

            if (path.Contains("POes"))
            {
                Assert.Contains("VendorNum eq 1323", path);
                return new POListResponse
                {
                    Value = new List<PODto>
                    {
                        new()
                        {
                            PONum = 3, VendorVendorID = "001058", VendorName = "LETS PRINT SA DE CV",
                            BuyerID = "LAF-CM13", BuyerIDName = "MONICA CASTILLO",
                            OrderDate = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
                            ShipName = "CARNES FINAS SAN JUAN LA FE", OpenOrder = true
                        }
                    }
                };
            }

            throw new InvalidOperationException($"Unexpected path: {path}");
        });
        var service = new PurchaseOrderHistoryService(client, July2026);

        var orders = await service.GetByVendorAsync("CFSJ_LAF", "001058", Creds);

        Assert.Single(orders);
        Assert.Equal(3, orders[0].PoNum);
        Assert.Equal("LETS PRINT SA DE CV", orders[0].VendorName);
        Assert.Equal("MONICA CASTILLO", orders[0].BuyerName);
    }

    [Fact]
    public async Task GetByVendorAsync_ExcludesOrdersOlderThan365Days()
    {
        // Matches the legacy app's carga_ocs_dias_atras = 365 constant
        // exactly, confirmed from its real source — "now" is 2026-07-15, so
        // the cutoff is 2025-07-15.
        var client = new StubEpicorClient(path =>
        {
            if (path.Contains("VendorSvc"))
                return new VendorNumListResponse { Value = new List<VendorNumDto> { new() { VendorNum = 1323 } } };

            return new POListResponse
            {
                Value = new List<PODto>
                {
                    new()
                    {
                        PONum = 1, VendorVendorID = "001058", VendorName = "LETS PRINT SA DE CV",
                        OrderDate = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero), OpenOrder = true,
                    },
                    new()
                    {
                        PONum = 2, VendorVendorID = "001058", VendorName = "LETS PRINT SA DE CV",
                        OrderDate = new DateTimeOffset(2025, 7, 20, 0, 0, 0, TimeSpan.Zero), OpenOrder = true,
                    },
                }
            };
        });
        var service = new PurchaseOrderHistoryService(client, July2026);

        var orders = await service.GetByVendorAsync("CFSJ_LAF", "001058", Creds);

        Assert.Single(orders);
        Assert.Equal(2, orders[0].PoNum);
    }

    [Fact]
    public async Task GetByVendorAsync_ExcludesClosedOrders()
    {
        // The legacy "Actualizar" handler hardcodes openOrder: true with no
        // UI toggle — closed orders never show, even within the 365-day
        // window.
        var client = new StubEpicorClient(path =>
        {
            if (path.Contains("VendorSvc"))
                return new VendorNumListResponse { Value = new List<VendorNumDto> { new() { VendorNum = 1323 } } };

            return new POListResponse
            {
                Value = new List<PODto>
                {
                    new()
                    {
                        PONum = 1, VendorVendorID = "001058", VendorName = "LETS PRINT SA DE CV",
                        OrderDate = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), OpenOrder = false,
                    },
                    new()
                    {
                        PONum = 2, VendorVendorID = "001058", VendorName = "LETS PRINT SA DE CV",
                        OrderDate = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), OpenOrder = true,
                    },
                }
            };
        });
        var service = new PurchaseOrderHistoryService(client, July2026);

        var orders = await service.GetByVendorAsync("CFSJ_LAF", "001058", Creds);

        Assert.Single(orders);
        Assert.Equal(2, orders[0].PoNum);
    }

    [Fact]
    public async Task GetByVendorAsync_ReturnsEmpty_WhenVendorNotFound()
    {
        var client = new StubEpicorClient(_ => new VendorNumListResponse());
        var service = new PurchaseOrderHistoryService(client, July2026);

        var orders = await service.GetByVendorAsync("CFSJ_LAF", "no-existe", Creds);

        Assert.Empty(orders);
    }

    [Fact]
    public async Task GetDetailAsync_JoinsPODetailsAndPORelsByLine()
    {
        // Line-level data (part, cost) lives on PODetail; release-level data
        // (release number, received/pending) lives on the separate PORel
        // table — confirmed live against the real Epicor server.
        var client = new StubEpicorClient(path =>
        {
            if (path.Contains("POes"))
            {
                Assert.Contains("$expand=PODetails", path);
                return new POWithDetailsDto
                {
                    PODetails = new List<PODetailDto>
                    {
                        new()
                        {
                            PONUM = 3, POLine = 1, PartNum = "EP09000022",
                            LineDesc = "BIENES NO DURADEROS", OrderQty = 1, IUM = "PZA", UnitCost = 2860m
                        }
                    }
                };
            }

            if (path.Contains("PORels"))
            {
                return new PORelListResponse
                {
                    Value = new List<PORelDto>
                    {
                        new() { PONum = 3, POLine = 1, PORelNum = 1, RelQty = 1m, ReceivedQty = 0.4m }
                    }
                };
            }

            throw new InvalidOperationException($"Unexpected path: {path}");
        });
        var service = new PurchaseOrderHistoryService(client, July2026);

        var lines = await service.GetDetailAsync("CFSJ_LAF", 3, Creds);

        Assert.Single(lines);
        var line = lines[0];
        Assert.Equal(1, line.Line);
        Assert.Equal(1, line.Rel);
        Assert.Equal("EP09000022", line.PartNum);
        Assert.Equal(2860m, line.UnitCost);
        Assert.Equal(0.4m, line.ReceivedQty);
        Assert.Equal(0.6m, line.PendingQty);
        Assert.Equal(2860m, line.Total); // RelQty (1) * UnitCost
    }

    [Fact]
    public async Task GetDetailAsync_ReturnsEmpty_WhenNoDetailLines()
    {
        var client = new StubEpicorClient(path =>
            path.Contains("POes") ? new POWithDetailsDto() : new PORelListResponse());
        var service = new PurchaseOrderHistoryService(client, July2026);

        var lines = await service.GetDetailAsync("CFSJ_LAF", 999, Creds);

        Assert.Empty(lines);
    }

    [Fact]
    public async Task GetDetailAsync_ShowsTheLineUsingOrderQty_WhenIrHasNoReleaseYet()
    {
        // Confirmed live: a PO created via OCACrearOC can have PODetail rows
        // with no matching PORel yet. The line must still show — using
        // OrderQty as the stand-in quantity — rather than silently
        // disappearing because the join found nothing on the PORel side.
        var client = new StubEpicorClient(path =>
        {
            if (path.Contains("POes"))
            {
                return new POWithDetailsDto
                {
                    PODetails = new List<PODetailDto>
                    {
                        new()
                        {
                            PONUM = 3224, POLine = 1, PartNum = "8323700247",
                            LineDesc = "CALABACITA KG", OrderQty = 12m, IUM = "KGS", UnitCost = 12m
                        }
                    }
                };
            }

            if (path.Contains("PORels")) return new PORelListResponse();

            throw new InvalidOperationException($"Unexpected path: {path}");
        });
        var service = new PurchaseOrderHistoryService(client, July2026);

        var lines = await service.GetDetailAsync("CFSJ_LAF", 3224, Creds);

        Assert.Single(lines);
        var line = lines[0];
        Assert.Equal(1, line.Line);
        Assert.Equal(0, line.Rel);
        Assert.Equal("8323700247", line.PartNum);
        Assert.Equal(12m, line.OrderQty);
        Assert.Equal(0m, line.ReceivedQty);
        Assert.Equal(12m, line.PendingQty);
        Assert.Equal(144m, line.Total);
    }
}
