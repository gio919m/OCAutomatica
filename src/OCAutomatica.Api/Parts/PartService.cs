using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Parts;

public sealed class PartService : IPartService
{
    private readonly IEpicorClient _epicor;

    public PartService(IEpicorClient epicor) => _epicor = epicor;

    public async Task<IReadOnlyList<PartRow>> GetByVendorAsync(
        string company,
        string plant,
        string vendorId,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        // BaqSvc/{BAQID} alone only returns the resource descriptor — the
        // actual rows live under the /Data sub-resource. CurrentCompany is
        // deliberately not sent: confirmed by live testing that the company
        // is already scoped by the URL's {Company} segment and passing it
        // made no difference to the result.
        var relativePath =
            $"BaqSvc/OCA_PartesPorProveedor/Data?CurrentPlant={Uri.EscapeDataString(plant)}" +
            $"&vendorId={Uri.EscapeDataString(vendorId)}";

        var response = await _epicor.GetAsync<PartListResponse>(
            company, relativePath, credentials, ct);

        if (response is null) return Array.Empty<PartRow>();

        return response.Value
            .Select(p => new PartRow(
                p.Vendor_VendorID,
                p.Vendor_Name,
                p.VendPart_PartNum,
                p.Part_PartDescription,
                p.Part_PUM,
                p.PartPlant_MinimumQty,
                p.PartPlant_MaximumQty,
                p.VendPart_BaseUnitPrice,
                p.PartPC_ProdCode ?? string.Empty,
                p.PartPC1_ProdCode ?? string.Empty,
                p.Calculated_Inventario,
                p.Calculated_CantidadEnTransito))
            .ToList();
    }
}
