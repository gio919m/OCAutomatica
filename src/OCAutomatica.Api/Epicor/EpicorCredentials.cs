namespace OCAutomatica.Api.Epicor;

/// <summary>
/// Epicor user credentials. Never serialize this type into an HTTP response.
/// </summary>
public sealed record EpicorCredentials(string Username, string Password)
{
    public override string ToString() => $"EpicorCredentials {{ Username = {Username} }}";
}
