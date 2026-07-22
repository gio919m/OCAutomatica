using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Parts;

public interface IPartService
{
    Task<IReadOnlyList<PartRow>> GetByVendorAsync(
        string company,
        string plant,
        string vendorId,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
