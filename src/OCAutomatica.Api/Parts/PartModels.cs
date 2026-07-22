namespace OCAutomatica.Api.Parts;

public sealed record PartRow(
    string VendorId,
    string VendorName,
    string PartNum,
    string PartDescription,
    string Uom,
    decimal MinimumQty,
    decimal MaximumQty,
    decimal Cost,
    string Ean13,
    string Ean14,
    decimal OnHandQty,
    decimal InTransitQty);

public sealed class PartListResponse
{
    public List<PartDto> Value { get; set; } = new();
}

// Field names confirmed by live verification against the test environment
// (Task 1, section 1.5/1.7). The BAQ was not given custom Display Names, so
// Epicor exposes joined fields with its default TableAlias_Field convention.
// Inventario/CantidadEnTransito are calculated fields sourced from subqueries,
// so Epicor prefixes them with the generic "Calculated_" alias (confirmed in
// the BAQ Designer's Display Fields grid: alias Calculated_Inventario, label
// "Inventario") rather than any table alias.
public sealed class PartDto
{
    public string Vendor_VendorID { get; set; } = string.Empty;
    public string Vendor_Name { get; set; } = string.Empty;
    public string VendPart_PartNum { get; set; } = string.Empty;
    public string Part_PartDescription { get; set; } = string.Empty;
    public string Part_PUM { get; set; } = string.Empty;
    public decimal PartPlant_MinimumQty { get; set; }
    public decimal PartPlant_MaximumQty { get; set; }
    public decimal VendPart_BaseUnitPrice { get; set; }
    public string? PartPC_ProdCode { get; set; }
    public string? PartPC1_ProdCode { get; set; }
    public decimal Calculated_Inventario { get; set; }
    public decimal Calculated_CantidadEnTransito { get; set; }
}
