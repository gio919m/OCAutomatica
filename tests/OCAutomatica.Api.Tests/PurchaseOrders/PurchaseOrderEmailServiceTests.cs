using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Organization;
using OCAutomatica.Api.PurchaseOrders;
using OCAutomatica.Api.Users;

namespace OCAutomatica.Api.Tests.PurchaseOrders;

public class PurchaseOrderEmailServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly Func<string, object?> _byPath;
        public StubEpicorClient(Func<string, object?> byPath) => _byPath = byPath;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_byPath(relativePath));

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by PurchaseOrderEmailService.");

        public Task<T?> InvokeFunctionAsync<T>(string company, string library, string function,
            object input, EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by PurchaseOrderEmailService.");
    }

    private sealed class StubUserDirectoryService : IUserDirectoryService
    {
        private readonly string? _email;
        public StubUserDirectoryService(string? email) => _email = email;
        public Task<string?> GetEmailAsync(string userId, EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult(_email);
    }

    private sealed class StubOrganizationService : IOrganizationService
    {
        private readonly IReadOnlyList<Plant> _plants;
        public StubOrganizationService(IReadOnlyList<Plant> plants) => _plants = plants;
        public Task<IReadOnlyList<Plant>> GetPlantsAsync(string company, EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult(_plants);
    }

    private sealed class FakeEmailQueueRepository : IEmailQueueRepository
    {
        public EmailQueueEntry? LastEntry { get; private set; }
        public Task InsertAsync(EmailQueueEntry entry, CancellationToken ct = default)
        {
            LastEntry = entry;
            return Task.CompletedTask;
        }
    }

    private static readonly EpicorCredentials Creds = new("epicor", "x");

    private static StubEpicorClient BuildVendorHeaderClient() => new(path =>
    {
        Assert.Contains("POes", path);
        return new PoVendorInfoDto { VendorVendorID = "001008", VendorName = "JARAMILLO TREVIÑO GERARDO MAGDALENO" };
    });

    [Fact]
    public async Task SendCopyToUserAsync_InsertsAQueueEntry_WithOcTipo2AndTheUsersOwnEmail()
    {
        var repository = new FakeEmailQueueRepository();
        var service = new PurchaseOrderEmailService(
            BuildVendorHeaderClient(),
            new StubUserDirectoryService("giovanni.montoya@carnessanjuan.com"),
            new StubOrganizationService(new List<Plant> { new("LAF", "LA FE") }),
            repository);

        var email = await service.SendCopyToUserAsync(
            "CFSJ_LAF", "CARNES FINAS SAN JUAN LA FE", "LAF", "epicor", 3431, Creds);

        Assert.Equal("giovanni.montoya@carnessanjuan.com", email);
        Assert.NotNull(repository.LastEntry);
        Assert.Equal("CFSJ_LAF", repository.LastEntry!.Company);
        Assert.Equal("CARNES FINAS SAN JUAN LA FE", repository.LastEntry.CompanyName);
        Assert.Equal("LAF", repository.LastEntry.Plant);
        Assert.Equal("LA FE", repository.LastEntry.PlantName);
        Assert.Equal("3431", repository.LastEntry.PoNumber);
        Assert.Equal("001008", repository.LastEntry.VendorId);
        Assert.Equal("JARAMILLO TREVIÑO GERARDO MAGDALENO", repository.LastEntry.VendorName);
        Assert.Equal("giovanni.montoya@carnessanjuan.com", repository.LastEntry.Emails);
        Assert.Equal(2, repository.LastEntry.OcTipo);
    }

    [Fact]
    public async Task SendCopyToUserAsync_Throws_WhenUserHasNoEmailCaptured()
    {
        var repository = new FakeEmailQueueRepository();
        var service = new PurchaseOrderEmailService(
            BuildVendorHeaderClient(),
            new StubUserDirectoryService(null),
            new StubOrganizationService(new List<Plant> { new("LAF", "LA FE") }),
            repository);

        await Assert.ThrowsAsync<InvalidUserEmailException>(() =>
            service.SendCopyToUserAsync("CFSJ_LAF", "CARNES FINAS SAN JUAN LA FE", "LAF", "epicor", 3431, Creds));

        Assert.Null(repository.LastEntry);
    }

    [Fact]
    public async Task SendCopyToUserAsync_Throws_WhenUserEmailIsMalformed()
    {
        var repository = new FakeEmailQueueRepository();
        var service = new PurchaseOrderEmailService(
            BuildVendorHeaderClient(),
            new StubUserDirectoryService("no-es-un-correo"),
            new StubOrganizationService(new List<Plant> { new("LAF", "LA FE") }),
            repository);

        await Assert.ThrowsAsync<InvalidUserEmailException>(() =>
            service.SendCopyToUserAsync("CFSJ_LAF", "CARNES FINAS SAN JUAN LA FE", "LAF", "epicor", 3431, Creds));

        Assert.Null(repository.LastEntry);
    }

    [Fact]
    public async Task SendCopyToUserAsync_FallsBackToThePlantCode_WhenPlantNameNotFound()
    {
        var repository = new FakeEmailQueueRepository();
        var service = new PurchaseOrderEmailService(
            BuildVendorHeaderClient(),
            new StubUserDirectoryService("giovanni.montoya@carnessanjuan.com"),
            new StubOrganizationService(Array.Empty<Plant>()),
            repository);

        await service.SendCopyToUserAsync("CFSJ_LAF", "CARNES FINAS SAN JUAN LA FE", "LAF", "epicor", 3431, Creds);

        Assert.Equal("LAF", repository.LastEntry!.PlantName);
    }
}
