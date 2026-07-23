using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

public sealed class PurchaseOrderHistoryService : IPurchaseOrderHistoryService
{
    // Matches the legacy WinForms app's carga_ocs_dias_atras constant exactly
    // (confirmed from its real source: FillVendorOCS filtered
    // "OrderDate >= Today - 365 days and OpenOrder = 1", hardcoded, no UI
    // toggle) — not "current calendar month" as first described in words.
    private const int LookbackDays = 365;

    private readonly IEpicorClient _epicor;
    private readonly TimeProvider _time;

    public PurchaseOrderHistoryService(IEpicorClient epicor, TimeProvider time)
    {
        _epicor = epicor;
        _time = time;
    }

    public async Task<IReadOnlyList<PurchaseOrderSummary>> GetByVendorAsync(
        string company,
        string vendorId,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        // Erp.PO filters by the internal VendorNum, not the display VendorID
        // ("Could not find a property named 'VendorID' on type 'Erp.PO'" —
        // confirmed live), so the vendor's VendorNum must be resolved first.
        var escapedVendorId = Uri.EscapeDataString(vendorId.Replace("'", "''"));
        var vendorPath =
            $"Erp.BO.VendorSvc/Vendors?$filter=VendorID eq '{escapedVendorId}'&$select=VendorNum&$top=1";
        var vendorResponse = await _epicor.GetAsync<VendorNumListResponse>(
            company, vendorPath, credentials, ct);
        var vendorNum = vendorResponse?.Value.FirstOrDefault()?.VendorNum;
        if (vendorNum is null) return Array.Empty<PurchaseOrderSummary>();

        var poPath =
            $"Erp.BO.POSvc/POes?$filter=VendorNum eq {vendorNum}" +
            "&$select=PONum,VendorVendorID,VendorName,BuyerID,BuyerIDName,OrderDate,ShipName,OpenOrder" +
            "&$orderby=PONum desc";
        var poResponse = await _epicor.GetAsync<POListResponse>(company, poPath, credentials, ct);
        if (poResponse is null) return Array.Empty<PurchaseOrderSummary>();

        // Filtered here in C# (not via OData $filter) since the exact
        // datetime/boolean-literal format Epicor's REST surface expects for
        // these hasn't been verified live, unlike the plain string/numeric
        // eq filters used elsewhere in this service.
        var now = _time.GetLocalNow();
        var cutoff = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset)
            .AddDays(-LookbackDays);

        return poResponse.Value
            .Where(p => p.OpenOrder && p.OrderDate >= cutoff)
            .Select(p => new PurchaseOrderSummary(
                p.PONum, p.VendorVendorID, p.VendorName, p.BuyerID, p.BuyerIDName,
                p.OrderDate, p.ShipName, p.OpenOrder))
            .ToList();
    }

    public async Task<IReadOnlyList<PurchaseOrderDetailLine>> GetDetailAsync(
        string company,
        int poNum,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        // Line-level data (part, cost) lives on PODetail; release-level data
        // (release number, received/pending quantities) lives on the
        // separate PORel table — Epicor's Header -> Line -> Release
        // structure. Joined here by POLine since neither table alone has
        // everything the grid needs.
        //
        // The flat `PODetails?$filter=PONUM eq {poNum}` collection query
        // reliably returns an empty array even for POs with confirmed real
        // lines (verified live against the real Epicor server, and
        // cross-checked against Epicor's own native UI, for PO 3223).
        // Fetching the header by key with $expand=PODetails is the shape
        // that actually returns the lines.
        var headerPath = $"Erp.BO.POSvc/POes('{company}',{poNum})?$expand=PODetails";
        var relPath =
            $"Erp.BO.POSvc/PORels?$filter=PONum eq {poNum}" +
            "&$select=PONum,POLine,PORelNum,RelQty,ReceivedQty";

        var headerTask = _epicor.GetAsync<POWithDetailsDto>(company, headerPath, credentials, ct);
        var relTask = _epicor.GetAsync<PORelListResponse>(company, relPath, credentials, ct);
        await Task.WhenAll(headerTask, relTask);

        var details = headerTask.Result?.PODetails ?? new List<PODetailDto>();
        var rels = relTask.Result?.Value ?? new List<PORelDto>();

        // Driven from PODetails, not PORels: a line's release(s) may not
        // exist yet (e.g. right after OCACrearOC creates the order), and a
        // line with no release must still show — using OrderQty as the
        // stand-in quantity — rather than silently disappearing.
        var lines = new List<PurchaseOrderDetailLine>();
        foreach (var detail in details)
        {
            var matchingRels = rels.Where(r => r.POLine == detail.POLine).ToList();

            if (matchingRels.Count == 0)
            {
                lines.Add(new PurchaseOrderDetailLine(
                    detail.POLine,
                    0,
                    detail.PartNum,
                    detail.LineDesc,
                    detail.OrderQty,
                    detail.IUM,
                    detail.UnitCost,
                    0,
                    detail.OrderQty,
                    detail.OrderQty * detail.UnitCost));
                continue;
            }

            foreach (var rel in matchingRels)
            {
                lines.Add(new PurchaseOrderDetailLine(
                    detail.POLine,
                    rel.PORelNum,
                    detail.PartNum,
                    detail.LineDesc,
                    rel.RelQty,
                    detail.IUM,
                    detail.UnitCost,
                    rel.ReceivedQty,
                    rel.RelQty - rel.ReceivedQty,
                    rel.RelQty * detail.UnitCost));
            }
        }

        return lines;
    }
}
