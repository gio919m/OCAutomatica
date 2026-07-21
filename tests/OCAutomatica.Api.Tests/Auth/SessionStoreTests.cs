using Microsoft.AspNetCore.DataProtection;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Tests.Auth;

public class SessionStoreTests
{
    private static SessionStore BuildStore()
    {
        var provider = DataProtectionProvider.Create("OCAutomatica.Tests");
        return new SessionStore(provider, TimeProvider.System);
    }

    [Fact]
    public void Create_ReturnsNonEmptySessionId()
    {
        var store = BuildStore();

        var sessionId = store.Create(new EpicorCredentials("jyanez", "secreto"));

        Assert.False(string.IsNullOrWhiteSpace(sessionId));
    }

    [Fact]
    public void Create_GeneratesDistinctIdsForEachSession()
    {
        var store = BuildStore();

        var first = store.Create(new EpicorCredentials("jyanez", "secreto"));
        var second = store.Create(new EpicorCredentials("jyanez", "secreto"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Get_ReturnsSessionWithUsername()
    {
        var store = BuildStore();
        var sessionId = store.Create(new EpicorCredentials("jyanez", "secreto"));

        var session = store.Get(sessionId);

        Assert.NotNull(session);
        Assert.Equal("jyanez", session!.Username);
    }

    [Fact]
    public void Get_ReturnsNullForUnknownSession()
    {
        var store = BuildStore();

        var session = store.Get("no-existe");

        Assert.Null(session);
    }

    [Fact]
    public void GetCredentials_RoundTripsPassword()
    {
        var store = BuildStore();
        var sessionId = store.Create(new EpicorCredentials("jyanez", "secreto"));

        var credentials = store.GetCredentials(sessionId);

        Assert.NotNull(credentials);
        Assert.Equal("jyanez", credentials!.Username);
        Assert.Equal("secreto", credentials.Password);
    }

    [Fact]
    public void SetContext_StoresCompanyPlantAndBuyer()
    {
        var store = BuildStore();
        var sessionId = store.Create(new EpicorCredentials("jyanez", "secreto"));

        store.SetContext(sessionId, "CFSJ_LAF", "MfgSys", "LAF-CM5");
        var session = store.Get(sessionId);

        Assert.Equal("CFSJ_LAF", session!.Company);
        Assert.Equal("MfgSys", session.Plant);
        Assert.Equal("LAF-CM5", session.BuyerId);
    }

    [Fact]
    public void Remove_DeletesTheSession()
    {
        var store = BuildStore();
        var sessionId = store.Create(new EpicorCredentials("jyanez", "secreto"));

        store.Remove(sessionId);

        Assert.Null(store.Get(sessionId));
    }
}
