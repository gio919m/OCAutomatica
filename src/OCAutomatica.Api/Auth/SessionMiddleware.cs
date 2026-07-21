namespace OCAutomatica.Api.Auth;

public sealed class SessionMiddleware
{
    public const string CookieName = "oca_session";
    public const string ItemKey = "UserSession";

    private readonly RequestDelegate _next;

    public SessionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ISessionStore sessions)
    {
        if (context.Request.Cookies.TryGetValue(CookieName, out var sessionId))
        {
            var session = sessions.Get(sessionId);
            if (session is not null)
            {
                context.Items[ItemKey] = session;
            }
        }

        await _next(context);
    }
}
