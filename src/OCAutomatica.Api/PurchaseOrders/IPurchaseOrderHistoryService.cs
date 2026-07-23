using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

public interface IPurchaseOrderHistoryService
{
    Task<IReadOnlyList<PurchaseOrderSummary>> GetByVendorAsync(
        string company,
        string vendorId,
        EpicorCredentials credentials,
        CancellationToken ct = default);

    Task<IReadOnlyList<PurchaseOrderDetailLine>> GetDetailAsync(
        string company,
        int poNum,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
