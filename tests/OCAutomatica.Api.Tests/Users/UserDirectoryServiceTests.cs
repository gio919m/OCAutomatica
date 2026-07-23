using Microsoft.Extensions.Options;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Users;

namespace OCAutomatica.Api.Tests.Users;

public class UserDirectoryServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly Func<string, object?> _byPath;
        public List<string> RequestedPaths { get; } = new();

        public StubEpicorClient(Func<string, object?> byPath) => _byPath = byPath;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
        {
            RequestedPaths.Add(relativePath);
            return Task.FromResult((T?)_byPath(relativePath));
        }

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by UserDirectoryService.");

        public Task<T?> InvokeFunctionAsync<T>(string company, string library, string function,
            object input, EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by UserDirectoryService.");
    }

    private static readonly EpicorCredentials Creds = new("epicor", "x");
    private static readonly IOptions<EpicorOptions> Options =
        Microsoft.Extensions.Options.Options.Create(new EpicorOptions { AnchorCompany = "CFSJ_LAF" });

    [Fact]
    public async Task GetEmailAsync_ReturnsTheCapturedEmail()
    {
        var client = new StubEpicorClient(path =>
        {
            Assert.Contains("UserID eq 'epicor'", path);
            return new UserEmailListResponse
            {
                Value = new List<UserEmailDto> { new() { EMailAddress = "giovanni.montoya@carnessanjuan.com" } }
            };
        });
        var service = new UserDirectoryService(client, Options);

        var email = await service.GetEmailAsync("epicor", Creds);

        Assert.Equal("giovanni.montoya@carnessanjuan.com", email);
    }

    [Fact]
    public async Task GetEmailAsync_ReturnsNull_WhenUserHasNoEmailCaptured()
    {
        var client = new StubEpicorClient(_ => new UserEmailListResponse
        {
            Value = new List<UserEmailDto> { new() { EMailAddress = "" } }
        });
        var service = new UserDirectoryService(client, Options);

        var email = await service.GetEmailAsync("sinCorreo", Creds);

        Assert.Null(email);
    }

    [Fact]
    public async Task GetEmailAsync_ReturnsNull_WhenUserNotFound()
    {
        var client = new StubEpicorClient(_ => new UserEmailListResponse());
        var service = new UserDirectoryService(client, Options);

        var email = await service.GetEmailAsync("noExiste", Creds);

        Assert.Null(email);
    }

    [Fact]
    public async Task GetEmailAsync_QueriesAnchoredAtAnchorCompany_NotTheActiveCompany()
    {
        // Ice.UserFile is system-wide, same as Ice.UserComp in AuthService —
        // the company segment in the URL is required by Epicor's REST
        // surface but irrelevant to which rows come back.
        string? companyUsed = null;
        var client = new CapturingStubEpicorClient((company, path) =>
        {
            companyUsed = company;
            return new UserEmailListResponse
            {
                Value = new List<UserEmailDto> { new() { EMailAddress = "x@y.com" } }
            };
        });
        var service = new UserDirectoryService(client, Options);

        await service.GetEmailAsync("epicor", Creds);

        Assert.Equal("CFSJ_LAF", companyUsed);
    }

    private sealed class CapturingStubEpicorClient : IEpicorClient
    {
        private readonly Func<string, string, object?> _byCompanyAndPath;
        public CapturingStubEpicorClient(Func<string, string, object?> byCompanyAndPath) => _byCompanyAndPath = byCompanyAndPath;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_byCompanyAndPath(company, relativePath));

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<T?> InvokeFunctionAsync<T>(string company, string library, string function,
            object input, EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
