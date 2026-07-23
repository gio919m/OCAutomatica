using Microsoft.Extensions.Options;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Users;

public sealed class UserDirectoryService : IUserDirectoryService
{
    // Ice.UserFile is system-wide (same as Ice.UserComp in AuthService) —
    // the company segment in the URL is required by Epicor's REST surface
    // but irrelevant to which user record comes back.
    private const string UserEmailPathTemplate =
        "Ice.BO.UserFileSvc/UserFiles?$filter=UserID eq '{0}'&$select=EMailAddress&$top=1";

    private readonly IEpicorClient _epicor;
    private readonly EpicorOptions _options;

    public UserDirectoryService(IEpicorClient epicor, IOptions<EpicorOptions> options)
    {
        _epicor = epicor;
        _options = options.Value;
    }

    public async Task<string?> GetEmailAsync(
        string userId,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var escaped = Uri.EscapeDataString(userId.Replace("'", "''"));
        var path = string.Format(UserEmailPathTemplate, escaped);

        var response = await _epicor.GetAsync<UserEmailListResponse>(
            _options.AnchorCompany, path, credentials, ct);

        var email = response?.Value.FirstOrDefault()?.EMailAddress;
        return string.IsNullOrWhiteSpace(email) ? null : email;
    }
}

public sealed class UserEmailListResponse
{
    public List<UserEmailDto> Value { get; set; } = new();
}

public sealed class UserEmailDto
{
    public string EMailAddress { get; set; } = string.Empty;
}
