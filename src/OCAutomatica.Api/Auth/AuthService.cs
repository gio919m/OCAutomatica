using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Auth;

public sealed class AuthService : IAuthService
{
    // Every buyer using this application must be able to read Vendors in
    // Epicor — it is the app's core purpose. CompanySvc was tried first and
    // rejected for the test user with an Access Scope/security error, which
    // is unrelated to whether the password is correct.
    private const string ProbePath = "Erp.BO.VendorSvc/Vendors?$top=1";

    private readonly IEpicorClient _epicor;

    public AuthService(IEpicorClient epicor) => _epicor = epicor;

    public async Task<bool> ValidateAsync(
        EpicorCredentials credentials,
        string company,
        CancellationToken ct = default)
    {
        try
        {
            await _epicor.GetAsync<object>(company, ProbePath, credentials, ct);
            return true;
        }
        catch (EpicorException ex) when (ex.Reason == EpicorErrorReason.InvalidCredentials)
        {
            return false;
        }
    }
}
