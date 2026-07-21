using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Organization;

public sealed class OrganizationService : IOrganizationService
{
    private const string PlantsPath = "Erp.BO.PlantSvc/Plants?$select=Plant1,Name";

    private readonly IEpicorClient _epicor;

    public OrganizationService(IEpicorClient epicor) => _epicor = epicor;

    public async Task<IReadOnlyList<Plant>> GetPlantsAsync(
        string company,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var response = await _epicor.GetAsync<PlantListResponse>(
            company, PlantsPath, credentials, ct);

        if (response is null) return Array.Empty<Plant>();

        return response.Value
            .Select(p => new Plant(p.Plant1, p.Name))
            .ToList();
    }
}
