namespace OCAutomatica.Api.Epicor;

public interface IEpicorClient
{
    Task<T?> GetAsync<T>(
        string company,
        string relativePath,
        EpicorCredentials credentials,
        CancellationToken ct = default);

    Task<T?> PostAsync<T>(
        string company,
        string relativePath,
        object body,
        EpicorCredentials credentials,
        CancellationToken ct = default);

    Task<T?> InvokeFunctionAsync<T>(
        string company,
        string library,
        string function,
        object input,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
