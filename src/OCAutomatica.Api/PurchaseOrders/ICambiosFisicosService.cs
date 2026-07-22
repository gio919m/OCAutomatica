using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

public interface ICambiosFisicosService
{
    Task<IReadOnlyList<CambioFisico>> GetPendingAsync(
        string company,
        string plant,
        string vendorId,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
