namespace OCAutomatica.Api.Epicor;

/// <summary>
/// Epicor user credentials. Never serialize this type into an HTTP response.
/// </summary>
public sealed record EpicorCredentials(string Username, string Password)
{
    /// <summary>
    /// The session ID Epicor's own Ice.Lib.SessionModSvc/Login returned for
    /// this user. When set, the call sends it back via the SessionInfo
    /// header (as {"SessionID":"..."}) so Ice.Lib.SessionModSvc/Logout can
    /// target that exact session instead of an untargetable implicit one.
    /// Only ever set for that one call — confirmed live that attaching it to
    /// unrelated business calls (e.g. Erp.BO.PlantSvc) makes Epicor 500.
    /// </summary>
    public string? EpicorSessionId { get; init; }

    public override string ToString() => $"EpicorCredentials {{ Username = {Username} }}";
}
