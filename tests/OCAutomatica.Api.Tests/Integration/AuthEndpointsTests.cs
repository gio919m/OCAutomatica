using System.Net;
using System.Net.Http.Json;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Tests.Integration;

/// <summary>
/// Exercises the full HTTP pipeline (SessionMiddleware + AuthController) for
/// the one distinction the login endpoint must never blur: a wrong password
/// (401) versus any other Epicor failure (503). Before this suite, that
/// mapping was only verified manually.
/// </summary>
public class AuthEndpointsTests
{
    private static readonly object LoginBody = new
    {
        username = "jyanez",
        password = "whatever",
        company = "CFSJ_LAF"
    };

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenPasswordIsWrong()
    {
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => throw new EpicorException(
            401, EpicorErrorReason.InvalidCredentials, "Invalid username or password.");

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", LoginBody);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("contrasena", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_ReturnsServiceUnavailable_WhenEpicorDeniesAccess()
    {
        // AccessDenied means the password was fine but the user's Epicor
        // security profile blocks the probe call. Must not be reported as a
        // wrong password (401) — that would send the user on a wild goose
        // chase resetting a password that was never the problem.
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => throw new EpicorException(
            401, EpicorErrorReason.AccessDenied, "Access denied (Erp.BO.Vendor.GetRows).");

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", LoginBody);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("contrasena", body, StringComparison.OrdinalIgnoreCase);
    }
}
