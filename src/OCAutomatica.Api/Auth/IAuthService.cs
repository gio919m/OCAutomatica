using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Auth;

public interface IAuthService
{
    /// <summary>
    /// Validates the credentials against Epicor and, in the same call, looks
    /// up which companies the user has access to (Ice.UserComp), with their
    /// display names. Returns null when Epicor rejects the credentials,
    /// an empty list when they're valid but the user has no company assigned,
    /// or the accessible companies otherwise.
    /// Any other failure (network, server error) propagates as EpicorException
    /// so it is never reported to the user as a wrong password.
    /// </summary>
    Task<IReadOnlyList<CompanyAccess>?> ValidateAsync(
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
