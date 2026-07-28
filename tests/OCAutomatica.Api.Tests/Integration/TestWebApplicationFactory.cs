using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Tests.Integration;

/// <summary>
/// Boots the real ASP.NET Core pipeline (middleware + controllers) in-memory,
/// replacing only the outbound Epicor dependency with a controllable stub so
/// tests never talk to a real Epicor server.
/// </summary>
public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    // Arbitrary Base64 test value — never the real Epicor Sign Key.
    public const string SsoSignKey = "dGVzdC1zc28tc2lnbi1rZXktZm9yLWludGVncmF0aW9uLXRlc3Rz";

    public TestEpicorClient EpicorClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IEpicorClient));
            services.AddSingleton<IEpicorClient>(EpicorClient);
            services.Configure<EpicorTokenOptions>(o =>
            {
                o.SignKey = SsoSignKey;
            });
        });
    }

    /// <summary>
    /// The login endpoint sets its session cookie with Secure = true. A plain
    /// http://localhost test client would have that cookie silently dropped,
    /// so tests that need the cookie to survive across requests (anything
    /// past login) must use this client instead of the plain CreateClient().
    /// </summary>
    public HttpClient CreateSecureClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost")
    });
}
