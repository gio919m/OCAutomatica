using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Organization;

public interface IOrganizationService
{
    Task<IReadOnlyList<Plant>> GetPlantsAsync(
        string company,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
