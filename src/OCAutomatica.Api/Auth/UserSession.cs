namespace OCAutomatica.Api.Auth;

/// <summary>
/// Server-side session state. Deliberately does not expose the password —
/// only ISessionStore.GetCredentials can decrypt it.
/// </summary>
public sealed class UserSession
{
    public required string SessionId { get; init; }
    public required string Username { get; init; }
    public string Company { get; set; } = string.Empty;
    public string Plant { get; set; } = string.Empty;
    public string? BuyerId { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}
