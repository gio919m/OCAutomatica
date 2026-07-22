using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Organization;

namespace OCAutomatica.Api.Tests.Organization;

public class OrganizationServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly object? _response;

        public StubEpicorClient(object? response) => _response = response;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_response);

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_response);

        public Task<T?> InvokeFunctionAsync<T>(
            string company, string library, string function, object input,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException(
                "InvokeFunctionAsync is not exercised by this test suite.");
    }

    private static readonly EpicorCredentials Creds = new("jyanez", "x");

    [Fact]
    public async Task GetPlantsAsync_MapsEpicorResponse()
    {
        var response = new PlantListResponse
        {
            Value = new List<PlantDto>
            {
                new() { Plant1 = "MfgSys", Name = "PLANTA LA FE" },
                new() { Plant1 = "RIO", Name = "PLANTA RIO" }
            }
        };
        var service = new OrganizationService(new StubEpicorClient(response));

        var plants = await service.GetPlantsAsync("CFSJ_LAF", Creds);

        Assert.Equal(2, plants.Count);
        Assert.Equal("MfgSys", plants[0].PlantId);
        Assert.Equal("PLANTA LA FE", plants[0].Name);
    }

    [Fact]
    public async Task GetPlantsAsync_ReturnsEmptyWhenNoResponse()
    {
        var service = new OrganizationService(new StubEpicorClient(null));

        var plants = await service.GetPlantsAsync("CFSJ_LAF", Creds);

        Assert.Empty(plants);
    }
}
