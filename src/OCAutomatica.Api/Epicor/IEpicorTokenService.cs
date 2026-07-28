namespace OCAutomatica.Api.Epicor;

public interface IEpicorTokenService
{
    /// <summary>
    /// Validates signature (HMACSHA256 with the shared Sign Key), issuer/audience
    /// ("epicor"), and expiration of a JWT issued by Epicor's own Token
    /// Authentication feature. Never throws for a malformed token — it is
    /// untrusted input from the client.
    /// </summary>
    /// <param name="expiresAtUtc">
    /// The token's own expiration, when valid — callers use this to size an
    /// app-side session to match exactly how long the underlying Epicor
    /// token stays usable. <see cref="DateTimeOffset.MinValue"/> when
    /// <paramref name="token"/> is invalid.
    /// </param>
    bool TryValidate(string token, out string username, out DateTimeOffset expiresAtUtc);
}
