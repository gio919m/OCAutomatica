using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace OCAutomatica.Api.Epicor;

public sealed class EpicorTokenService : IEpicorTokenService
{
    private const string ExpectedIssuer = "epicor";

    private readonly EpicorTokenOptions _options;
    private readonly TimeProvider _time;

    public EpicorTokenService(IOptions<EpicorTokenOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public bool TryValidate(string token, out string username, out DateTimeOffset expiresAtUtc)
    {
        username = string.Empty;
        expiresAtUtc = DateTimeOffset.MinValue;

        if (string.IsNullOrEmpty(_options.SignKey)) return false;

        var parts = token.Split('.');
        if (parts.Length != 3) return false;

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = Base64UrlDecode(parts[1]);
            signature = Base64UrlDecode(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        byte[] expectedSignature;
        try
        {
            expectedSignature = ComputeSignature(signingInput);
        }
        catch (FormatException)
        {
            // SignKey itself isn't valid Base64 (e.g. misconfigured appsettings).
            return false;
        }

        if (signature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(signature, expectedSignature))
        {
            return false;
        }

        JwtPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<JwtPayload>(payloadBytes);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload is null) return false;
        if (payload.Iss != ExpectedIssuer) return false;
        // Aud is NOT checked against a fixed value: confirmed live that Epicor
        // itself issues "epicor" for a fresh interactive login but a
        // "00000000-..." GUID for a token Kinetic silently renewed in the
        // background — both are genuinely valid (Epicor's own REST API
        // accepts both as Bearer auth). Iss plus the signature check below
        // are what actually prove the token came from Epicor.
        if (string.IsNullOrWhiteSpace(payload.Username)) return false;
        if (!long.TryParse(payload.Exp, out var exp)) return false;
        if (_time.GetUtcNow().ToUnixTimeSeconds() >= exp) return false;

        username = payload.Username;
        expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(exp);
        return true;
    }

    private byte[] ComputeSignature(byte[] input)
    {
        using var hmac = new HMACSHA256(Convert.FromBase64String(_options.SignKey));
        return hmac.ComputeHash(input);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty
        };
        return Convert.FromBase64String(padded);
    }

    // exp/iat are strings (not numbers) in Epicor's own tokens — confirmed
    // live by decoding a real token issued by Kinetic's Token Authentication.
    private sealed class JwtPayload
    {
        [JsonPropertyName("exp")] public string Exp { get; set; } = string.Empty;
        [JsonPropertyName("iat")] public string Iat { get; set; } = string.Empty;
        [JsonPropertyName("iss")] public string Iss { get; set; } = string.Empty;
        [JsonPropertyName("aud")] public string Aud { get; set; } = string.Empty;
        [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    }
}
