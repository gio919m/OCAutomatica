using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Vendors;

public interface IVendorService
{
    Task<IReadOnlyList<Vendor>> SearchAsync(
        string company,
        string search,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
