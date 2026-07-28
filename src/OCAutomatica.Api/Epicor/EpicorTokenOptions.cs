namespace OCAutomatica.Api.Epicor;

/// <summary>
/// Settings for validating/re-issuing JWTs from Epicor Kinetic's native
/// "Token Authentication" feature (Server Management → Application Server →
/// Configure Token Authentication).
/// </summary>
public sealed class EpicorTokenOptions
{
    public const string SectionName = "EpicorToken";

    /// <summary>Sign Key shown in Epicor's Token Authentication settings (Base64, standard — not URL-safe).</summary>
    public string SignKey { get; set; } = string.Empty;

    /// <summary>Lifetime (seconds) of tokens this app re-issues. Should match the session cookie's MaxAge (8h = 28800).</summary>
    public int SessionLifetimeSeconds { get; set; } = 28800;
}
