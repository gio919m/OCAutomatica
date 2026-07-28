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

    public bool TryValidate(string token, out string username)
    {
        username = string.Empty;

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
        if (payload.Iss != ExpectedIssuer || payload.Aud != ExpectedIssuer) return false;
        if (string.IsNullOrWhiteSpace(payload.Username)) return false;
        if (!long.TryParse(payload.Exp, out var exp)) return false;
        if (_time.GetUtcNow().ToUnixTimeSeconds() >= exp) return false;

        username = payload.Username;
        return true;
    }

    public string IssueSessionToken(string username)
    {
        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        var exp = now + _options.SessionLifetimeSeconds;

        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new JwtPayload
        {
            Exp = exp.ToString(),
            Iat = now.ToString(),
            Iss = ExpectedIssuer,
            Aud = ExpectedIssuer,
            Username = username
        }));

        var signingInput = Encoding.UTF8.GetBytes($"{header}.{payload}");
        var signature = Base64UrlEncode(ComputeSignature(signingInput));

        return $"{header}.{payload}.{signature}";
    }

    private byte[] ComputeSignature(byte[] input)
    {
        using var hmac = new HMACSHA256(Convert.FromBase64String(_options.SignKey));
        return hmac.ComputeHash(input);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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
