using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Tests.Integration;

/// <summary>
/// Configurable stand-in for the real Epicor client used by integration tests.
/// Every service under test (AuthService, BuyerService, OrganizationService)
/// only ever calls GetAsync, so tests route behavior by inspecting the
/// relative path, exactly like they would with the real Epicor REST v2 API.
/// </summary>
public sealed class TestEpicorClient : IEpicorClient
{
    /// <summary>
    /// Invoked for every GetAsync call. Return the response object to embed
    /// (cast to T), or throw an EpicorException to simulate an Epicor error.
    /// Defaults to returning null for any path that isn't configured.
    /// </summary>
    public Func<string, string, EpicorCredentials, object?> OnGet { get; set; }
        = (_, _, _) => null;

    public Task<T?> GetAsync<T>(
        string company,
        string relativePath,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var result = OnGet(company, relativePath, credentials);
        return Task.FromResult((T?)result);
    }

    public Task<T?> PostAsync<T>(
        string company,
        string relativePath,
        object body,
        EpicorCredentials credentials,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "PostAsync is not exercised by the endpoints covered by these integration tests.");

    public Task<T?> InvokeFunctionAsync<T>(
        string company, string library, string function, object input,
        EpicorCredentials credentials, CancellationToken ct = default)
        => throw new NotSupportedException(
            "InvokeFunctionAsync is not exercised by the endpoints covered by these integration tests.");
}
