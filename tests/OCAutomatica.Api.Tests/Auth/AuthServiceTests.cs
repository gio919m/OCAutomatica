using Microsoft.Extensions.Options;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Tests.Auth;

public class AuthServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly Func<object?> _behavior;

        public StubEpicorClient(Func<object?> behavior) => _behavior = behavior;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_behavior());

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_behavior());

        public Task<T?> InvokeFunctionAsync<T>(
            string company, string library, string function, object input,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException(
                "InvokeFunctionAsync is not exercised by this test suite.");
    }

    private static IOptions<EpicorOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new EpicorOptions { AnchorCompany = "CFSJ_LAF" });

    [Fact]
    public async Task ValidateAsync_ReturnsAccessibleCompanies()
    {
        var client = new StubEpicorClient(() => new UserCompListResponse
        {
            Value = new List<UserCompDto>
            {
                new() { Company = "CFSJ_LAF", CompanyName = "Carnes Finas San Juan Laredo" },
                new() { Company = "CFSJ_ANA", CompanyName = "Carnes Finas San Juan Anahuac" },
            }
        });
        var service = new AuthService(client, Options());

        var result = await service.ValidateAsync(new EpicorCredentials("jyanez", "correcta"));

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Contains(result, c => c.Company == "CFSJ_ANA" && c.CompanyName == "Carnes Finas San Juan Anahuac");
    }

    [Fact]
    public async Task ValidateAsync_ReturnsEmptyList_WhenUserHasNoCompanies()
    {
        var client = new StubEpicorClient(() => new UserCompListResponse());
        var service = new AuthService(client, Options());

        var result = await service.ValidateAsync(new EpicorCredentials("jyanez", "correcta"));

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNullOnInvalidCredentials()
    {
        var client = new StubEpicorClient(() => throw new EpicorException(
            401, EpicorErrorReason.InvalidCredentials, "Invalid username or password."));
        var service = new AuthService(client, Options());

        var result = await service.ValidateAsync(new EpicorCredentials("jyanez", "incorrecta"));

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_PropagatesAccessDenied()
    {
        // The password can be correct while the user still lacks rights to
        // Ice.BO.UserFileSvc in Epicor's own security. That is not a login
        // failure and must not be reported as one.
        var client = new StubEpicorClient(() => throw new EpicorException(
            401, EpicorErrorReason.AccessDenied, "Access denied (Ice.BO.UserFile.GetRows)."));
        var service = new AuthService(client, Options());

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            service.ValidateAsync(new EpicorCredentials("jyanez", "correcta")));

        Assert.Equal(EpicorErrorReason.AccessDenied, ex.Reason);
    }

    [Fact]
    public async Task ValidateAsync_PropagatesOtherErrors()
    {
        var client = new StubEpicorClient(() => throw new EpicorException(
            503, EpicorErrorReason.Other, "service unavailable"));
        var service = new AuthService(client, Options());

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            service.ValidateAsync(new EpicorCredentials("jyanez", "x")));

        Assert.Equal(503, ex.StatusCode);
    }
}
