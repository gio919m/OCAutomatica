using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Auth;

public sealed class SessionStore : ISessionStore
{
    private const string ProtectorPurpose = "OCAutomatica.SessionCredentials";

    private sealed record Entry(UserSession Session, string ProtectedPassword);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;

    public SessionStore(IDataProtectionProvider protectionProvider, TimeProvider time)
    {
        _protector = protectionProvider.CreateProtector(ProtectorPurpose);
        _time = time;
    }

    public string Create(EpicorCredentials credentials)
    {
        var sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var session = new UserSession
        {
            SessionId = sessionId,
            Username = credentials.Username,
            LastSeenUtc = _time.GetUtcNow()
        };

        _entries[sessionId] = new Entry(session, _protector.Protect(credentials.Password));
        return sessionId;
    }

    public UserSession? Get(string sessionId)
    {
        if (!_entries.TryGetValue(sessionId, out var entry)) return null;

        entry.Session.LastSeenUtc = _time.GetUtcNow();
        return entry.Session;
    }

    public EpicorCredentials? GetCredentials(string sessionId)
    {
        if (!_entries.TryGetValue(sessionId, out var entry)) return null;

        var password = _protector.Unprotect(entry.ProtectedPassword);
        return new EpicorCredentials(entry.Session.Username, password);
    }

    public void SetContext(string sessionId, string company, string plant, string? buyerId)
    {
        if (!_entries.TryGetValue(sessionId, out var entry)) return;

        entry.Session.Company = company;
        entry.Session.Plant = plant;
        entry.Session.BuyerId = buyerId;
    }

    public void Remove(string sessionId) => _entries.TryRemove(sessionId, out _);
}
