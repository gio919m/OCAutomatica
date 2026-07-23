using System.Net;
using System.Net.Http.Json;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Tests.Integration;

/// <summary>
/// Exercises the full HTTP pipeline (SessionMiddleware + AuthController) for
/// the distinctions the login endpoint must never blur: a wrong password
/// (401) versus any other Epicor failure (503) versus a valid user with no
/// companies assigned (403). Before this suite, that mapping was only
/// verified manually.
/// </summary>
public class AuthEndpointsTests
{
    private static readonly object LoginBody = new
    {
        username = "jyanez",
        password = "whatever"
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
        // security profile blocks the UserComps lookup. Must not be reported
        // as a wrong password (401) — that would send the user on a wild
        // goose chase resetting a password that was never the problem.
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => throw new EpicorException(
            401, EpicorErrorReason.AccessDenied, "Access denied (Ice.BO.UserFile.GetRows).");

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", LoginBody);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("contrasena", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_ReturnsForbidden_WhenUserHasNoCompanies()
    {
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => new UserCompListResponse();

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", LoginBody);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Login_AutoSelectsCompany_WhenUserHasExactlyOne()
    {
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => new UserCompListResponse
        {
            Value = new List<UserCompDto>
            {
                new() { Company = "CFSJ_LAF", CompanyName = "Carnes Finas San Juan Laredo" }
            }
        };

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", LoginBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponseBody>();
        Assert.Equal("CFSJ_LAF", body!.Company);
        Assert.Single(body.Companies);
    }

    [Fact]
    public async Task Login_LeavesCompanyUnset_WhenUserHasMultiple()
    {
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => new UserCompListResponse
        {
            Value = new List<UserCompDto>
            {
                new() { Company = "CFSJ_LAF", CompanyName = "Carnes Finas San Juan Laredo" },
                new() { Company = "CFSJ_ANA", CompanyName = "Carnes Finas San Juan Anahuac" }
            }
        };

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", LoginBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponseBody>();
        Assert.Equal(string.Empty, body!.Company);
        Assert.Equal(2, body.Companies.Count);
    }

    [Fact]
    public async Task SelectCompany_ReturnsConflict_WhenCompanyAlreadySet()
    {
        // Once fixed, a purchase order created later is stamped with this
        // company — it must never be re-selectable without a fresh login.
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => new UserCompListResponse
        {
            Value = new List<UserCompDto>
            {
                new() { Company = "CFSJ_LAF", CompanyName = "Carnes Finas San Juan Laredo" },
                new() { Company = "CFSJ_ANA", CompanyName = "Carnes Finas San Juan Anahuac" }
            }
        };

        using var client = factory.CreateSecureClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", LoginBody);
        loginResponse.EnsureSuccessStatusCode();

        var first = await client.PostAsJsonAsync("/api/auth/company", new { company = "CFSJ_LAF" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/auth/company", new { company = "CFSJ_ANA" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var me = await client.GetFromJsonAsync<LoginResponseBody>("/api/auth/me");
        Assert.Equal("CFSJ_LAF", me!.Company);
    }

    [Fact]
    public async Task SelectCompany_Succeeds_WhenResubmittingTheSameCompany()
    {
        // A page reload re-renders CompanyPicker with no memory of the
        // earlier choice — resubmitting the SAME company (not a switch) must
        // be a harmless no-op, or the user is stuck with no way forward.
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => new UserCompListResponse
        {
            Value = new List<UserCompDto>
            {
                new() { Company = "CFSJ_LAF", CompanyName = "Carnes Finas San Juan Laredo" },
                new() { Company = "CFSJ_ANA", CompanyName = "Carnes Finas San Juan Anahuac" }
            }
        };

        using var client = factory.CreateSecureClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", LoginBody);
        loginResponse.EnsureSuccessStatusCode();

        var first = await client.PostAsJsonAsync("/api/auth/company", new { company = "CFSJ_LAF" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var again = await client.PostAsJsonAsync("/api/auth/company", new { company = "CFSJ_LAF" });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        var body = await again.Content.ReadFromJsonAsync<LoginResponseBody>();
        Assert.Equal("CFSJ_LAF", body!.Company);
    }

    [Fact]
    public async Task Logout_ReleasesTheEpicorSession_WhenCompanyIsSet()
    {
        // Epicor holds its own session (and license) open until
        // Ice.Lib.SessionModSvc/Logout is called explicitly — our own
        // sign-out must trigger it, or the license stays held indefinitely.
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => new UserCompListResponse
        {
            Value = new List<UserCompDto>
            {
                new() { Company = "CFSJ_LAF", CompanyName = "Carnes Finas San Juan Laredo" }
            }
        };

        string? loggedOutCompany = null;
        factory.EpicorClient.OnPost = (company, relativePath, _, _) =>
        {
            if (relativePath == "Ice.Lib.SessionModSvc/Logout") loggedOutCompany = company;
            return null;
        };

        using var client = factory.CreateSecureClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", LoginBody);
        loginResponse.EnsureSuccessStatusCode();

        var logoutResponse = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.Equal("CFSJ_LAF", loggedOutCompany);
    }

    [Fact]
    public async Task Logout_SendsTheSessionIdLoginReturned_AsTheSessionInfoHeader()
    {
        // Ice.Lib.SessionModSvc/Login returns a session ID that only THIS
        // exact SessionInfo header value can later release via Logout — a
        // Logout call without it just opens (and immediately closes) an
        // unrelated session, leaving the real one held open. Confirmed live
        // against the real Epicor server before writing this.
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => new UserCompListResponse
        {
            Value = new List<UserCompDto>
            {
                new() { Company = "CFSJ_LAF", CompanyName = "Carnes Finas San Juan Laredo" }
            }
        };

        string? sessionIdSentToLogout = null;
        factory.EpicorClient.OnPost = (_, relativePath, _, credentials) =>
        {
            if (relativePath == "Ice.Lib.SessionModSvc/Login")
            {
                return new EpicorLoginResponse { ReturnObj = "3fa85f64-5717-4562-b3fc-2c963f66afa6" };
            }
            if (relativePath == "Ice.Lib.SessionModSvc/Logout")
            {
                sessionIdSentToLogout = credentials.EpicorSessionId;
            }
            return null;
        };

        using var client = factory.CreateSecureClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", LoginBody);
        loginResponse.EnsureSuccessStatusCode();

        await client.PostAsync("/api/auth/logout", null);

        Assert.Equal("3fa85f64-5717-4562-b3fc-2c963f66afa6", sessionIdSentToLogout);
    }

    [Fact]
    public async Task Logout_StillSucceeds_WhenEpicorSessionReleaseFails()
    {
        // A held license is bad, but blocking the user's own sign-out because
        // Epicor's cleanup call failed would be worse.
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => new UserCompListResponse
        {
            Value = new List<UserCompDto>
            {
                new() { Company = "CFSJ_LAF", CompanyName = "Carnes Finas San Juan Laredo" }
            }
        };
        factory.EpicorClient.OnPost = (_, _, _, _) => throw new EpicorException(
            503, EpicorErrorReason.Other, "service unavailable");

        using var client = factory.CreateSecureClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", LoginBody);
        loginResponse.EnsureSuccessStatusCode();

        var logoutResponse = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
    }

    private sealed record CompanyOptionBody(string Company, string CompanyName);
    private sealed record LoginResponseBody(
        string Username, string Company, string Plant,
        string? BuyerId, string? BuyerName, List<CompanyOptionBody> Companies);
}
