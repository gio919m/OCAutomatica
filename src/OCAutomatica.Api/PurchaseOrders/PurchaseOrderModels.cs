namespace OCAutomatica.Api.PurchaseOrders;

public sealed record PurchaseOrderLine(string PartNum, decimal Cantidad, decimal Costo, string Uom);

public sealed record CreatePurchaseOrderRequest(
    string VendorId,
    string Comentarios,
    IReadOnlyList<PurchaseOrderLine> Lineas);

public sealed record CreatePurchaseOrderResult(int PoNum);

// Shape of the JSON body sent to OCACrearOC — property names must match the
// Function's input parameter names exactly (Task 3, section 3.3).
public sealed class PurchaseOrderFunctionInput
{
    public string Plant { get; set; } = string.Empty;
    public string VendorID { get; set; } = string.Empty;
    public string BuyerID { get; set; } = string.Empty;
    public string Comentarios { get; set; } = string.Empty;

    // The Function's Lineas parameter is a plain string, not a tableset - there is no
    // ready-made Epicor TableSet with PartNum/Cantidad/Costo/UOM columns, and building a
    // Business Object/UBAQ just to get one isn't worth it here. This carries a JSON-serialized
    // array (built in PurchaseOrderService.CreateAsync), parsed inside the Function's own code.
    public string Lineas { get; set; } = string.Empty;
}

public sealed class PurchaseOrderLineInput
{
    public string PartNum { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal Costo { get; set; }
    public string UOM { get; set; } = string.Empty;
}

// Shape of the JSON response from OCACrearOC (Task 3, section 3.3).
public sealed class PurchaseOrderFunctionOutput
{
    public int PONum { get; set; }
}
