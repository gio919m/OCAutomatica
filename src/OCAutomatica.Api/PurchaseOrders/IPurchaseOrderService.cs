using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

public interface IPurchaseOrderService
{
    Task<CreatePurchaseOrderResult> CreateAsync(
        string company,
        string plant,
        string buyerId,
        CreatePurchaseOrderRequest request,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
