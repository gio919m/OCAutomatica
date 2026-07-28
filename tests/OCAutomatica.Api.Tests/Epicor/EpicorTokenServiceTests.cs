using System.Text;
using Microsoft.Extensions.Options;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Tests.Epicor;

public class EpicorTokenServiceTests
{
    // Arbitrary Base64 test value — never the real Epicor Sign Key.
    private const string TestSignKey = "dGVzdC1zaWduLWtleS1mb3ItdW5pdC10ZXN0cw==";
    private const string OtherSignKey = "YW5vdGhlci1jb21wbGV0ZWx5LWRpZmZlcmVudC1rZXk=";

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    private static EpicorTokenService BuildService(
        string signKey = TestSignKey, int lifetimeSeconds = 28800, DateTimeOffset? at = null) =>
        new(
            Options.Create(new EpicorTokenOptions { SignKey = signKey, SessionLifetimeSeconds = lifetimeSeconds }),
            new FixedTimeProvider(at ?? Now));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Builds a raw JWT independently of EpicorTokenService, so tests
    /// never validate the implementation against itself.</summary>
    private static string BuildRawToken(
        string signKey, string iss, string aud, string username, long iat, long exp)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var payloadJson =
            $$"""{"exp":"{{exp}}","iat":"{{iat}}","iss":"{{iss}}","aud":"{{aud}}","username":"{{username}}"}""";
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = Encoding.UTF8.GetBytes($"{header}.{payload}");
        using var hmac = new System.Security.Cryptography.HMACSHA256(Convert.FromBase64String(signKey));
        var signature = Base64UrlEncode(hmac.ComputeHash(signingInput));
        return $"{header}.{payload}.{signature}";
    }

    [Fact]
    public void IssueSessionToken_ProducesATokenTryValidateAccepts()
    {
        var service = BuildService();

        var token = service.IssueSessionToken("epicor");

        Assert.True(service.TryValidate(token, out var username));
        Assert.Equal("epicor", username);
    }

    [Fact]
    public void TryValidate_RejectsATokenSignedWithADifferentKey()
    {
        var issuer = BuildService(signKey: OtherSignKey);
        var validator = BuildService(signKey: TestSignKey);

        var token = issuer.IssueSessionToken("epicor");

        Assert.False(validator.TryValidate(token, out _));
    }

    [Fact]
    public void TryValidate_RejectsAnExpiredToken()
    {
        var issuer = BuildService(lifetimeSeconds: 3600, at: Now);
        var token = issuer.IssueSessionToken("epicor");

        var validator = BuildService(at: Now.AddSeconds(3601));

        Assert.False(validator.TryValidate(token, out _));
    }

    [Fact]
    public void TryValidate_AcceptsATokenAtTheExactExpirationBoundaryMinusOneSecond()
    {
        var issuer = BuildService(lifetimeSeconds: 3600, at: Now);
        var token = issuer.IssueSessionToken("epicor");

        var validator = BuildService(at: Now.AddSeconds(3599));

        Assert.True(validator.TryValidate(token, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    [InlineData("a.b.c")]
    public void TryValidate_RejectsAMalformedToken(string malformed)
    {
        var service = BuildService();

        Assert.False(service.TryValidate(malformed, out var username));
        Assert.Equal(string.Empty, username);
    }

    [Fact]
    public void TryValidate_RejectsAWellSignedTokenWithTheWrongIssuer()
    {
        var service = BuildService();
        var now = Now.ToUnixTimeSeconds();
        var forged = BuildRawToken(TestSignKey, iss: "not-epicor", aud: "epicor", username: "epicor",
            iat: now, exp: now + 3600);

        Assert.False(service.TryValidate(forged, out _));
    }

    [Fact]
    public void TryValidate_RejectsAWellSignedTokenWithTheWrongAudience()
    {
        var service = BuildService();
        var now = Now.ToUnixTimeSeconds();
        var forged = BuildRawToken(TestSignKey, iss: "epicor", aud: "not-epicor", username: "epicor",
            iat: now, exp: now + 3600);

        Assert.False(service.TryValidate(forged, out _));
    }

    [Fact]
    public void TryValidate_RejectsATokenWhoseSignatureWasTamperedWith()
    {
        var service = BuildService();
        var token = service.IssueSessionToken("epicor");
        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{parts[2][..^2]}xx";

        Assert.False(service.TryValidate(tampered, out _));
    }
}
