namespace OCAutomatica.Api.Epicor;

/// <summary>
/// Epicor returns HTTP 401 for three unrelated situations, distinguished only
/// by the ErrorMessage text in the response body: bad credentials, a bad
/// x-api-key, and a valid user denied access to a specific Business Object.
/// Callers must not treat every 401 as "wrong password".
/// </summary>
public enum EpicorErrorReason
{
    InvalidCredentials,
    InvalidApiKey,
    AccessDenied,
    Other
}

public sealed class EpicorException : Exception
{
    public int StatusCode { get; }
    public EpicorErrorReason Reason { get; }

    public EpicorException(int statusCode, EpicorErrorReason reason, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Reason = reason;
    }
}
