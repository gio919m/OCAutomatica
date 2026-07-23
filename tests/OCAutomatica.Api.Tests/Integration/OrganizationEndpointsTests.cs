using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Buyers;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Tests.Integration;

/// <summary>
/// Exercises the full HTTP pipeline (SessionMiddleware + OrganizationController)
/// for the CanCreateOrders flag: it must flip based on whether the user has a
/// default buyer in Epicor, which previously had no automated coverage.
/// </summary>
public class OrganizationEndpointsTests
{
    private static readonly object LoginBody = new
    {
        username = "jyanez",
        password = "correct"
    };

    private static readonly UserCompListResponse SingleCompany = new()
    {
        Value = new List<UserCompDto>
        {
            new() { Company = "CFSJ_LAF", CompanyName = "Carnes Finas San Juan Laredo" }
        }
    };

    private static readonly object SetContextBody = new { plant = "MfgSys" };

    /// <summary>
    /// Logs in through the real endpoint (so SessionMiddleware sees a genuine
    /// session cookie) and returns a client primed with that cookie, ready to
    /// call organization endpoints.
    /// </summary>
    private static async Task<HttpClient> LoginAsync(TestWebApplicationFactory factory)
    {
        var client = factory.CreateSecureClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", LoginBody);
        response.EnsureSuccessStatusCode();
        return client;
    }

    private static object RouteByPath(
        string relativePath,
        BuyerListResponse buyerResponse)
    {
        if (relativePath.Contains("UserFileSvc", StringComparison.OrdinalIgnoreCase))
        {
            return SingleCompany; // login resolves to exactly one company: CFSJ_LAF
        }

        if (relativePath.Contains("PurAgentSvc", StringComparison.OrdinalIgnoreCase))
        {
            return buyerResponse;
        }

        throw new InvalidOperationException($"Unexpected Epicor call in test: {relativePath}");
    }

    [Fact]
    public async Task SetContext_ReturnsCanCreateOrdersFalse_WhenUserHasNoDefaultBuyer()
    {
        using var factory = new TestWebApplicationFactory();
        var noBuyers = new BuyerListResponse { Value = new List<BuyerDto>() };
        factory.EpicorClient.OnGet = (_, relativePath, _) => RouteByPath(relativePath, noBuyers);

        using var client = await LoginAsync(factory);
        var response = await client.PostAsJsonAsync("/api/organization/context", SetContextBody);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.False(root.GetProperty("canCreateOrders").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("buyerId").ValueKind);
    }

    [Fact]
    public async Task SetContext_ReturnsCanCreateOrdersTrue_WhenUserHasDefaultBuyer()
    {
        using var factory = new TestWebApplicationFactory();
        var withBuyer = new BuyerListResponse
        {
            Value = new List<BuyerDto>
            {
                new()
                {
                    BuyerID = "LAF-CM9",
                    Name = "JANNETH YANEZ",
                    PurAuths = new List<PurAuthDto>
                    {
                        new() { DcdUserID = "jyanez", IsPrimaryUser = true }
                    }
                }
            }
        };
        factory.EpicorClient.OnGet = (_, relativePath, _) => RouteByPath(relativePath, withBuyer);

        using var client = await LoginAsync(factory);
        var response = await client.PostAsJsonAsync("/api/organization/context", SetContextBody);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.True(root.GetProperty("canCreateOrders").GetBoolean());
        Assert.Equal("LAF-CM9", root.GetProperty("buyerId").GetString());
    }

    [Fact]
    public async Task SetContext_ReturnsConflict_WhenPlantAlreadySet()
    {
        // Once fixed, a purchase order created later is stamped with this
        // plant — it must never be re-selectable without a fresh login.
        using var factory = new TestWebApplicationFactory();
        var withBuyer = new BuyerListResponse
        {
            Value = new List<BuyerDto>
            {
                new()
                {
                    BuyerID = "LAF-CM9",
                    Name = "JANNETH YANEZ",
                    PurAuths = new List<PurAuthDto>
                    {
                        new() { DcdUserID = "jyanez", IsPrimaryUser = true }
                    }
                }
            }
        };
        factory.EpicorClient.OnGet = (_, relativePath, _) => RouteByPath(relativePath, withBuyer);

        using var client = await LoginAsync(factory);

        var first = await client.PostAsJsonAsync("/api/organization/context", SetContextBody);
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            "/api/organization/context", new { plant = "OtherPlant" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task SetContext_Succeeds_WhenResubmittingTheSamePlant()
    {
        // A page reload re-renders ContextPicker with no memory of the
        // earlier choice — resubmitting the SAME plant (not a switch) must
        // be a harmless no-op, or the user is stuck with no way forward.
        using var factory = new TestWebApplicationFactory();
        var withBuyer = new BuyerListResponse
        {
            Value = new List<BuyerDto>
            {
                new()
                {
                    BuyerID = "LAF-CM9",
                    Name = "JANNETH YANEZ",
                    PurAuths = new List<PurAuthDto>
                    {
                        new() { DcdUserID = "jyanez", IsPrimaryUser = true }
                    }
                }
            }
        };
        factory.EpicorClient.OnGet = (_, relativePath, _) => RouteByPath(relativePath, withBuyer);

        using var client = await LoginAsync(factory);

        var first = await client.PostAsJsonAsync("/api/organization/context", SetContextBody);
        first.EnsureSuccessStatusCode();

        var again = await client.PostAsJsonAsync("/api/organization/context", SetContextBody);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        using var document = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
        Assert.Equal("LAF-CM9", document.RootElement.GetProperty("buyerId").GetString());
    }
}
