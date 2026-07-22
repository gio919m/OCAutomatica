using System.Text.Json;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

public sealed class PurchaseOrderService : IPurchaseOrderService
{
    private readonly IEpicorClient _epicor;

    public PurchaseOrderService(IEpicorClient epicor) => _epicor = epicor;

    public async Task<CreatePurchaseOrderResult> CreateAsync(
        string company,
        string plant,
        string buyerId,
        CreatePurchaseOrderRequest request,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var lineas = request.Lineas
            .Select(l => new PurchaseOrderLineInput
            {
                PartNum = l.PartNum,
                Cantidad = l.Cantidad,
                Costo = l.Costo,
                UOM = l.Uom
            })
            .ToList();

        var input = new PurchaseOrderFunctionInput
        {
            Plant = plant,
            VendorID = request.VendorId,
            BuyerID = buyerId,
            Comentarios = request.Comentarios,
            Lineas = JsonSerializer.Serialize(lineas)
        };

        var response = await _epicor.InvokeFunctionAsync<PurchaseOrderFunctionOutput>(
            company, "OCACrearOC", "OCACrearOC", input, credentials, ct);

        if (response is null)
        {
            throw new EpicorException(
                500, EpicorErrorReason.Other, "Epicor no devolvio un numero de orden.");
        }

        return new CreatePurchaseOrderResult(response.PONum);
    }
}
