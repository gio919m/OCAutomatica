namespace OCAutomatica.Api.Epicor;

/// <summary>
/// Settings for validating JWTs from Epicor Kinetic's native "Token
/// Authentication" feature (Server Management → Application Server →
/// Configure Token Authentication).
/// </summary>
public sealed class EpicorTokenOptions
{
    public const string SectionName = "EpicorToken";

    /// <summary>Sign Key shown in Epicor's Token Authentication settings (Base64, standard — not URL-safe).</summary>
    public string SignKey { get; set; } = string.Empty;
}
