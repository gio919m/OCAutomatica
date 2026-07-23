namespace OCAutomatica.Api.PurchaseOrders;

public sealed record PurchaseOrderSummary(
    int PoNum,
    string VendorId,
    string VendorName,
    string BuyerId,
    string BuyerName,
    DateTimeOffset? OrderDate,
    string ShipName,
    bool OpenOrder);

public sealed record PurchaseOrderDetailLine(
    int Line,
    int Rel,
    string PartNum,
    string Description,
    decimal OrderQty,
    string Uom,
    decimal UnitCost,
    decimal ReceivedQty,
    decimal PendingQty,
    decimal Total);

// --- Raw Epicor response shapes ---

public sealed class VendorNumListResponse
{
    public List<VendorNumDto> Value { get; set; } = new();
}

public sealed class VendorNumDto
{
    public int VendorNum { get; set; }
}

public sealed class POListResponse
{
    public List<PODto> Value { get; set; } = new();
}

public sealed class PODto
{
    public int PONum { get; set; }
    public string VendorVendorID { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public string BuyerID { get; set; } = string.Empty;
    public string BuyerIDName { get; set; } = string.Empty;
    public DateTimeOffset? OrderDate { get; set; }
    public string ShipName { get; set; } = string.Empty;
    public bool OpenOrder { get; set; }
}

public sealed class PODetailListResponse
{
    public List<PODetailDto> Value { get; set; } = new();
}

// Flat `PODetails?$filter=PONUM eq {poNum}` reliably returns an empty array
// even for POs with confirmed real lines (verified live against the real
// Epicor server for PO 3223, cross-checked against Epicor's own native UI).
// The reliable shape is the keyed header fetched with $expand=PODetails.
public sealed class POWithDetailsDto
{
    public List<PODetailDto> PODetails { get; set; } = new();
}

public sealed class PODetailDto
{
    // Epicor exposes this key as "PONUM" (all caps) on PODetail, unlike the
    // "PONum" used on the PO header row itself — confirmed live.
    public int PONUM { get; set; }
    public int POLine { get; set; }
    public string PartNum { get; set; } = string.Empty;
    public string LineDesc { get; set; } = string.Empty;
    public decimal OrderQty { get; set; }
    public string IUM { get; set; } = string.Empty;
    public decimal UnitCost { get; set; }
}

public sealed class PORelListResponse
{
    public List<PORelDto> Value { get; set; } = new();
}

public sealed class PORelDto
{
    public int PONum { get; set; }
    public int POLine { get; set; }
    public int PORelNum { get; set; }
    public decimal RelQty { get; set; }
    public decimal ReceivedQty { get; set; }
}
