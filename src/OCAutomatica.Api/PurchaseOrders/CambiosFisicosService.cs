using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

public sealed class CambiosFisicosService : ICambiosFisicosService
{
    private readonly IEpicorClient _epicor;

    public CambiosFisicosService(IEpicorClient epicor) => _epicor = epicor;

    public async Task<IReadOnlyList<CambioFisico>> GetPendingAsync(
        string company,
        string plant,
        string vendorId,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        // BaqSvc/{BAQID} alone only returns the resource descriptor — the
        // actual rows live under the /Data sub-resource. CurrentCompany is
        // deliberately not sent, same as OCA_PartesPorProveedor in Plan 2:
        // confirmed by live testing that the company is already scoped by
        // the URL's {Company} segment.
        var relativePath =
            $"BaqSvc/OCA_CambiosFisicos/Data?CurrentPlant={Uri.EscapeDataString(plant)}" +
            $"&vendorId={Uri.EscapeDataString(vendorId)}";

        var response = await _epicor.GetAsync<CambioFisicoListResponse>(
            company, relativePath, credentials, ct);

        if (response is null) return Array.Empty<CambioFisico>();

        return response.Value
            .Select(r => new CambioFisico(
                r.UD104A_Character01,
                r.UD104A_Character02,
                r.UD104A_Character04,
                r.Calculated_CantidadPendiente,
                r.UD104A_Character06))
            .ToList();
    }
}
