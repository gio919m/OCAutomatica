using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Vendors;

public sealed class VendorService : IVendorService
{
    private readonly IEpicorClient _epicor;

    public VendorService(IEpicorClient epicor) => _epicor = epicor;

    public async Task<IReadOnlyList<Vendor>> SearchAsync(
        string company,
        string search,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        // OData escapes an embedded single quote by doubling it.
        var escaped = search.Replace("'", "''");
        var relativePath =
            $"Erp.BO.VendorSvc/Vendors?$filter=contains(Name,'{escaped}') or contains(VendorID,'{escaped}')&$top=20";

        var response = await _epicor.GetAsync<VendorListResponse>(
            company, relativePath, credentials, ct);

        if (response is null) return Array.Empty<Vendor>();

        return response.Value
            .Select(v => new Vendor(v.VendorID, v.Name))
            .ToList();
    }
}
