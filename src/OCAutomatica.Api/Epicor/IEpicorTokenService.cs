namespace OCAutomatica.Api.Epicor;

public interface IEpicorTokenService
{
    /// <summary>
    /// Validates signature (HMACSHA256 with the shared Sign Key), issuer/audience
    /// ("epicor"), and expiration of a JWT issued by Epicor's own Token
    /// Authentication feature. Never throws for a malformed token — it is
    /// untrusted input from the client.
    /// </summary>
    bool TryValidate(string token, out string username);

    /// <summary>
    /// Mints a new token in the exact same format, signed with the same Sign
    /// Key, for <paramref name="username"/>. Callers must only ever pass a
    /// username that came from a successful TryValidate call on an
    /// Epicor-issued token — never a client-supplied value, since the Sign
    /// Key can mint a valid token for any username with no password check.
    /// </summary>
    string IssueSessionToken(string username);
}
