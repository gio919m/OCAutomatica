using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Users;

public interface IUserDirectoryService
{
    Task<string?> GetEmailAsync(
        string userId,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
