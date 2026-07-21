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
    }

    [Fact]
    public async Task ValidateAsync_ReturnsTrueWhenEpicorAccepts()
    {
        var client = new StubEpicorClient(() => new object());
        var service = new AuthService(client);

        var result = await service.ValidateAsync(
            new EpicorCredentials("jyanez", "correcta"), "CFSJ_LAF");

        Assert.True(result);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsFalseOnInvalidCredentials()
    {
        var client = new StubEpicorClient(() => throw new EpicorException(
            401, EpicorErrorReason.InvalidCredentials, "Invalid username or password."));
        var service = new AuthService(client);

        var result = await service.ValidateAsync(
            new EpicorCredentials("jyanez", "incorrecta"), "CFSJ_LAF");

        Assert.False(result);
    }

    [Fact]
    public async Task ValidateAsync_PropagatesAccessDenied()
    {
        // The password can be correct while the user still lacks rights to
        // Erp.BO.VendorSvc in Epicor's own security. That is not a login
        // failure and must not be reported as one.
        var client = new StubEpicorClient(() => throw new EpicorException(
            401, EpicorErrorReason.AccessDenied, "Access denied (Erp.BO.Vendor.GetRows)."));
        var service = new AuthService(client);

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            service.ValidateAsync(new EpicorCredentials("jyanez", "correcta"), "CFSJ_LAF"));

        Assert.Equal(EpicorErrorReason.AccessDenied, ex.Reason);
    }

    [Fact]
    public async Task ValidateAsync_PropagatesOtherErrors()
    {
        var client = new StubEpicorClient(() => throw new EpicorException(
            503, EpicorErrorReason.Other, "service unavailable"));
        var service = new AuthService(client);

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            service.ValidateAsync(new EpicorCredentials("jyanez", "x"), "CFSJ_LAF"));

        Assert.Equal(503, ex.StatusCode);
    }
}
