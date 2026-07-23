using Microsoft.Extensions.Options;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Auth;

public sealed class AuthService : IAuthService
{
    // Ice.UserComp is a system-wide table (like Ice.Company) — Epicor's REST
    // URL still requires a company segment, but the anchor company doesn't
    // need to be one the user actually has access to; it's confirmed to
    // return every company assignment for the given UserID regardless.
    private const string UserCompsPathTemplate =
        "Ice.BO.UserFileSvc/UserComps?$filter=UserID eq '{0}'&$select=Company,CompanyName";

    private readonly IEpicorClient _epicor;
    private readonly EpicorOptions _options;

    public AuthService(IEpicorClient epicor, IOptions<EpicorOptions> options)
    {
        _epicor = epicor;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<CompanyAccess>?> ValidateAsync(
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        // OData escapes an embedded single quote by doubling it, then the
        // result must be percent-encoded so characters like & # % can't
        // corrupt the query string.
        var escaped = Uri.EscapeDataString(credentials.Username.Replace("'", "''"));
        var path = string.Format(UserCompsPathTemplate, escaped);

        try
        {
            var response = await _epicor.GetAsync<UserCompListResponse>(
                _options.AnchorCompany, path, credentials, ct);

            if (response is null) return Array.Empty<CompanyAccess>();

            return response.Value
                .Select(v => new CompanyAccess(v.Company, v.CompanyName))
                .ToList();
        }
        catch (EpicorException ex) when (ex.Reason == EpicorErrorReason.InvalidCredentials)
        {
            return null;
        }
    }
}
