namespace OCAutomatica.Api.PurchaseOrders;

public sealed record PurchaseOrderReportData(
    int PoNum,
    string PlantName,
    bool Approved,
    DateTimeOffset? OrderDate,
    DateTimeOffset PrintedAt,
    string VendorId,
    string VendorName,
    string VendorAddress1,
    string VendorAddress2,
    string VendorCity,
    string DeliveryAddress,
    string ShipViaDescription,
    string BuyerName,
    string CommentText,
    string BuyerEmail,
    IReadOnlyList<PurchaseOrderReportLine> Lines,
    decimal Subtotal,
    decimal Taxes,
    decimal Withholdings,
    decimal Total);

public sealed record PurchaseOrderReportLine(
    int Line,
    string PartNum,
    string Description,
    string Ean,
    decimal OrderQty,
    string Uom,
    decimal UnitCost,
    decimal ExtendedPrice);

// --- Raw Epicor response shapes ---

public sealed class PurchaseOrderReportHeaderDto
{
    public int PONum { get; set; }
    public bool Approve { get; set; }
    public string ApprovalStatus { get; set; } = string.Empty;
    public string BuyerIDName { get; set; } = string.Empty;
    public string VendorVendorID { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public DateTimeOffset? OrderDate { get; set; }
    public string ShipViaCode { get; set; } = string.Empty;
    public string CommentText { get; set; } = string.Empty;
    public string ShipAddress1 { get; set; } = string.Empty;
    public string ShipAddress2 { get; set; } = string.Empty;
    public string ShipCity { get; set; } = string.Empty;
    public string ShipState { get; set; } = string.Empty;
    public string ShipZIP { get; set; } = string.Empty;
    public decimal DocTotalTax { get; set; }
    public decimal TotalWhTax { get; set; }
    public List<PODetailDto> PODetails { get; set; } = new();
}

public sealed class VendorAddressListResponse
{
    public List<VendorAddressDto> Value { get; set; } = new();
}

public sealed class VendorAddressDto
{
    public string Address1 { get; set; } = string.Empty;
    public string Address2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}

public sealed class CompanyAddressListResponse
{
    public List<CompanyAddressDto> Value { get; set; } = new();
}

public sealed class CompanyAddressDto
{
    public string Address1 { get; set; } = string.Empty;
    public string Address2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Zip { get; set; } = string.Empty;
    public string StateTaxID { get; set; } = string.Empty;
}

public sealed class PlantDetailsListResponse
{
    public List<PlantDetailsDto> Value { get; set; } = new();
}

public sealed class PlantDetailsDto
{
    public string Name { get; set; } = string.Empty;
    public string PhoneNum { get; set; } = string.Empty;
}

public sealed class ShipViaListResponse
{
    public List<ShipViaDto> Value { get; set; } = new();
}

public sealed class ShipViaDto
{
    public string Description { get; set; } = string.Empty;
}

public sealed class PartEanListResponse
{
    public List<PartEanDto> Value { get; set; } = new();
}

public sealed class PartEanDto
{
    public string PartNum { get; set; } = string.Empty;
    public string UOMCode { get; set; } = string.Empty;
    public string PRODCODE { get; set; } = string.Empty;
}
