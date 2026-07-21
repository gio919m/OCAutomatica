using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Auth;

public interface IAuthService
{
    /// <summary>
    /// Returns true when Epicor accepts the credentials, false when it rejects them.
    /// Any other failure (network, server error) propagates as EpicorException so it
    /// is never reported to the user as a wrong password.
    /// </summary>
    Task<bool> ValidateAsync(
        EpicorCredentials credentials,
        string company,
        CancellationToken ct = default);
}
