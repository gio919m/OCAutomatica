namespace OCAutomatica.Api.PurchaseOrders;

// Domain model exposed to the frontend. The legacy query never gives these
// fields semantic meaning either — it just concatenates them for display in
// the PO comment (built independently by the Epicor Function in Task 3).
// This preview is purely informative for the buyer, so the fields keep their
// raw, unrenamed identity.
public sealed record CambioFisico(
    string Character01,
    string Character02,
    string Character04,
    decimal CantidadPendiente,
    string Character06);

public sealed class CambioFisicoListResponse
{
    public List<CambioFisicoDto> Value { get; set; } = new();
}

// Field names confirmed by live verification against the test environment
// (see plan3-task-4-brief.md). The BAQ was not given custom Display Names,
// so Epicor exposes the UD104A table's fields with its default
// TableAlias_Field convention, and the summed quantity with the generic
// "Calculated_" alias (same pattern as PartDto in Plan 2).
public sealed class CambioFisicoDto
{
    public string UD104A_Character01 { get; set; } = string.Empty;
    public string UD104A_Character02 { get; set; } = string.Empty;
    public string UD104A_Character04 { get; set; } = string.Empty;
    public decimal Calculated_CantidadPendiente { get; set; }
    public string UD104A_Character06 { get; set; } = string.Empty;
}
