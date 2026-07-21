using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Auth;

public interface ISessionStore
{
    string Create(EpicorCredentials credentials);
    UserSession? Get(string sessionId);
    EpicorCredentials? GetCredentials(string sessionId);
    void SetContext(string sessionId, string company, string plant, string? buyerId);
    void Remove(string sessionId);
}
