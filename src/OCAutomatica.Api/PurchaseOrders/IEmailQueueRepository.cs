namespace OCAutomatica.Api.PurchaseOrders;

/// <summary>
/// One row to insert into interfaz_envia_oc — same columns sp_EnviaOC_V2
/// fills, minus fechaInserto/fechaUltimaRevision/fechaProceso/estatus/mensaje,
/// which the repository sets to their fixed initial values itself.
/// </summary>
public sealed record EmailQueueEntry(
    string Company,
    string CompanyName,
    string Plant,
    string PlantName,
    string PoNumber,
    string VendorId,
    string VendorName,
    string Emails,
    int OcTipo);

public interface IEmailQueueRepository
{
    Task InsertAsync(EmailQueueEntry entry, CancellationToken ct = default);
}
