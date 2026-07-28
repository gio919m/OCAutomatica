using Microsoft.AspNetCore.Mvc;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    public sealed record LoginRequest(string Username, string Password);

    /// <summary>
    /// Site is accepted but intentionally unused here — plant auto-selection
    /// happens client-side via the existing POST /api/organization/context,
    /// not inside this action. It's part of this DTO only so the frontend's
    /// call site can pass all three URL params uniformly.
    /// </summary>
    public sealed record SsoLoginRequest(string Token, string? Company, string? Site);
    public sealed record SelectCompanyRequest(string Company);
    public sealed record CompanyOption(string Company, string CompanyName);
    public sealed record SessionResponse(
        string Username,
        string Company,
        string Plant,
        string? BuyerId,
        string? BuyerName,
        IReadOnlyList<CompanyOption> Companies);

    private readonly IAuthService _auth;
    private readonly ISessionStore _sessions;
    private readonly IEpicorClient _epicor;
    private readonly IEpicorTokenService _tokens;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService auth,
        ISessionStore sessions,
        IEpicorClient epicor,
        IEpicorTokenService tokens,
        ILogger<AuthController> logger)
    {
        _auth = auth;
        _sessions = sessions;
        _epicor = epicor;
        _tokens = tokens;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var credentials = new EpicorCredentials(request.Username, request.Password);

        IReadOnlyList<CompanyAccess>? companies;
        try
        {
            companies = await _auth.ValidateAsync(credentials, ct);
        }
        catch (EpicorException ex)
        {
            // AuthService already turns InvalidCredentials into companies=null,
            // so only InvalidApiKey, AccessDenied, or Other reach this catch.
            // None of them are a wrong password and must not be reported as one.
            _logger.LogError(ex,
                "Epicor error ({Reason}) while validating {Username}",
                ex.Reason, request.Username);

            var message = ex.Reason switch
            {
                EpicorErrorReason.InvalidApiKey =>
                    "La aplicacion no pudo autenticarse con Epicor (clave de API invalida). Avisa a sistemas.",
                EpicorErrorReason.AccessDenied =>
                    "Tu usuario de Epicor no tiene permiso para consultar sus companias. Pide que revisen tu perfil de seguridad en Epicor.",
                _ =>
                    "No se pudo contactar a Epicor. Intenta de nuevo o avisa a sistemas."
            };

            return StatusCode(503, new { message });
        }

        if (companies is null)
        {
            _logger.LogWarning("Rejected login for {Username}", request.Username);
            return Unauthorized(new { message = "Usuario o contrasena incorrectos." });
        }

        if (companies.Count == 0)
        {
            _logger.LogWarning("User {Username} has no companies assigned in Epicor", request.Username);
            return StatusCode(403, new
            {
                message = "Tu usuario no tiene companias asignadas en Epicor. Contacta a sistemas."
            });
        }

        var sessionId = _sessions.Create(credentials);
        _sessions.SetAvailableCompanies(sessionId, companies);

        var selectedCompany = companies.Count == 1 ? companies[0].Company : string.Empty;
        _sessions.SetContext(sessionId, selectedCompany, string.Empty, null);

        if (selectedCompany.Length > 0)
        {
            await EstablishEpicorSessionAsync(sessionId, selectedCompany, credentials, ct);
        }

        Response.Cookies.Append(SessionMiddleware.CookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            MaxAge = TimeSpan.FromHours(8)
        });

        _logger.LogInformation("Login succeeded for {Username} ({CompanyCount} companies)",
            request.Username, companies.Count);

        return Ok(new SessionResponse(
            request.Username,
            selectedCompany,
            string.Empty,
            null,
            null,
            companies.Select(c => new CompanyOption(c.Company, c.CompanyName)).ToList()));
    }

    [HttpPost("sso-login")]
    public async Task<IActionResult> SsoLogin(SsoLoginRequest request, CancellationToken ct)
    {
        if (!_tokens.TryValidate(request.Token, out var username, out var expiresAtUtc))
        {
            return Unauthorized(new { message = "Token de Epicor invalido o expirado." });
        }

        var originalCredentials = new EpicorCredentials(username, string.Empty)
        {
            BearerToken = request.Token
        };

        IReadOnlyList<CompanyAccess>? companies;
        try
        {
            companies = await _auth.ValidateAsync(originalCredentials, ct);
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while validating SSO token for {Username}",
                ex.Reason, username);

            var message = ex.Reason switch
            {
                EpicorErrorReason.InvalidApiKey =>
                    "La aplicacion no pudo autenticarse con Epicor (clave de API invalida). Avisa a sistemas.",
                EpicorErrorReason.AccessDenied =>
                    "Tu usuario de Epicor no tiene permiso para consultar sus companias. Pide que revisen tu perfil de seguridad en Epicor.",
                _ =>
                    "No se pudo contactar a Epicor. Intenta de nuevo o avisa a sistemas."
            };

            return StatusCode(503, new { message });
        }

        if (companies is null)
        {
            _logger.LogWarning("Rejected SSO login for {Username} (Epicor did not accept the token)", username);
            return Unauthorized(new { message = "Token de Epicor invalido o expirado." });
        }

        if (companies.Count == 0)
        {
            _logger.LogWarning("User {Username} has no companies assigned in Epicor", username);
            return StatusCode(403, new
            {
                message = "Tu usuario no tiene companias asignadas en Epicor. Contacta a sistemas."
            });
        }

        // Reuses the original Kinetic-issued token for the whole session,
        // rather than minting our own — confirmed live that a token this app
        // mints itself (even for the same user) makes Epicor open a second,
        // untied session/license, while the original token is recognized as
        // the same session already open in Kinetic and opens none. The cost
        // is that this session can only last as long as the original token
        // does (see the cookie's MaxAge below), instead of a fixed 8 hours.
        var credentials = originalCredentials;

        var sessionId = _sessions.Create(credentials);
        _sessions.SetAvailableCompanies(sessionId, companies);

        // The URL's company param is client-supplied and must be validated
        // against the user's real Epicor companies before trusting it — an
        // explicitly requested company that doesn't match must leave the
        // session with no company selected (same as today's multi-company
        // login with no selection), never silently fall back to a different
        // company just because the user happens to only have one.
        var urlCompanyIsValid = !string.IsNullOrEmpty(request.Company) &&
            companies.Any(c => c.Company == request.Company);
        var selectedCompany = urlCompanyIsValid
            ? request.Company!
            : string.IsNullOrEmpty(request.Company) && companies.Count == 1
                ? companies[0].Company
                : string.Empty;
        _sessions.SetContext(sessionId, selectedCompany, string.Empty, null);

        if (selectedCompany.Length > 0)
        {
            await EstablishEpicorSessionAsync(sessionId, selectedCompany, credentials, ct);
        }

        Response.Cookies.Append(SessionMiddleware.CookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            MaxAge = expiresAtUtc - DateTimeOffset.UtcNow
        });

        _logger.LogInformation("SSO login succeeded for {Username} ({CompanyCount} companies)",
            username, companies.Count);

        return Ok(new SessionResponse(
            username,
            selectedCompany,
            string.Empty,
            null,
            null,
            companies.Select(c => new CompanyOption(c.Company, c.CompanyName)).ToList()));
    }

    [HttpPost("company")]
    public async Task<IActionResult> SelectCompany(SelectCompanyRequest request, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
        {
            return Unauthorized();
        }

        // Company is fixed for the lifetime of the session — every purchase
        // order created afterwards is stamped with it. Changing to a
        // DIFFERENT company mid-session (without a fresh login) must never
        // be possible.
        if (!string.IsNullOrEmpty(session.Company))
        {
            if (!string.Equals(session.Company, request.Company, StringComparison.Ordinal))
            {
                return Conflict(new
                {
                    message = "La compania ya esta fijada para esta sesion. Cierra sesion para cambiar de compania."
                });
            }

            // Re-submitting the same company — a harmless no-op caused by a
            // page reload re-rendering the picker with no memory of the
            // earlier choice. Must not re-run setup or clobber whatever
            // plant may already be fixed on this session.
            return Ok(new SessionResponse(
                session.Username,
                session.Company,
                session.Plant,
                session.BuyerId,
                null,
                session.AvailableCompanies.Select(c => new CompanyOption(c.Company, c.CompanyName)).ToList()));
        }

        var match = session.AvailableCompanies
            .FirstOrDefault(c => string.Equals(c.Company, request.Company, StringComparison.Ordinal));

        if (match is null)
        {
            return BadRequest(new { message = "Compania no valida para este usuario." });
        }

        _sessions.SetContext(session.SessionId, match.Company, string.Empty, null);

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is not null)
        {
            await EstablishEpicorSessionAsync(session.SessionId, match.Company, credentials, ct);
        }

        return Ok(new SessionResponse(
            session.Username,
            match.Company,
            string.Empty,
            null,
            null,
            session.AvailableCompanies.Select(c => new CompanyOption(c.Company, c.CompanyName)).ToList()));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue(SessionMiddleware.CookieName, out var sessionId))
        {
            await ReleaseEpicorSessionAsync(sessionId, ct);
            _sessions.Remove(sessionId);
        }

        Response.Cookies.Delete(SessionMiddleware.CookieName);
        return NoContent();
    }

    /// <summary>
    /// Calls Ice.Lib.SessionModSvc/Login explicitly and remembers the
    /// returned session ID on our own session. Without this, every Epicor
    /// call still works fine (Basic Auth alone is enough), but Epicor tracks
    /// an implicit session we have no ID for and can therefore never target
    /// for release — this ID is what lets ReleaseEpicorSessionAsync later
    /// point Logout at this exact session via the SessionInfo header.
    /// Best-effort: if this fails, the app still works over plain Basic Auth;
    /// we simply won't be able to release the license on sign-out.
    /// </summary>
    private async Task EstablishEpicorSessionAsync(
        string sessionId, string company, EpicorCredentials credentials, CancellationToken ct)
    {
        try
        {
            var response = await _epicor.PostAsync<EpicorLoginResponse>(
                company, "Ice.Lib.SessionModSvc/Login", new { }, credentials, ct);

            if (response is not null && !string.IsNullOrEmpty(response.ReturnObj))
            {
                _sessions.SetEpicorSessionId(sessionId, response.ReturnObj);
            }
        }
        catch (EpicorException ex)
        {
            _logger.LogWarning(ex,
                "Could not establish an explicit Epicor session on {Company}", company);
        }
    }

    /// <summary>
    /// Best-effort attempt to release Epicor's own session on sign-out.
    /// Epicor keeps its own session alive (and its license held) until
    /// Ice.Lib.SessionModSvc/Logout is called with a SessionInfo header
    /// ({"SessionID":"..."}) naming the exact session created by Login —
    /// confirmed live against the real server. A failure here must never
    /// block the user's own sign-out.
    /// </summary>
    private async Task ReleaseEpicorSessionAsync(string sessionId, CancellationToken ct)
    {
        var session = _sessions.Get(sessionId);
        var credentials = _sessions.GetCredentials(sessionId);
        if (session is null || credentials is null || string.IsNullOrEmpty(session.Company))
        {
            return;
        }

        var logoutCredentials = string.IsNullOrEmpty(session.EpicorSessionId)
            ? credentials
            : credentials with { EpicorSessionId = session.EpicorSessionId };

        try
        {
            await _epicor.PostAsync<object>(
                session.Company, "Ice.Lib.SessionModSvc/Logout", new { }, logoutCredentials, ct);
        }
        catch (EpicorException ex)
        {
            _logger.LogWarning(ex,
                "Could not release the Epicor session for {Username} on {Company}",
                session.Username, session.Company);
        }
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
        {
            return Unauthorized();
        }

        return Ok(new SessionResponse(
            session.Username,
            session.Company,
            session.Plant,
            session.BuyerId,
            null,
            session.AvailableCompanies.Select(c => new CompanyOption(c.Company, c.CompanyName)).ToList()));
    }
}
