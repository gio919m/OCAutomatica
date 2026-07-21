using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Buyers;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    public sealed record LoginRequest(string Username, string Password, string Company);
    public sealed record SessionResponse(
        string Username, string Company, string Plant, string? BuyerId, string? BuyerName);

    private readonly IAuthService _auth;
    private readonly ISessionStore _sessions;
    private readonly IBuyerService _buyers;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService auth,
        ISessionStore sessions,
        IBuyerService buyers,
        ILogger<AuthController> logger)
    {
        _auth = auth;
        _sessions = sessions;
        _buyers = buyers;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var credentials = new EpicorCredentials(request.Username, request.Password);

        bool accepted;
        try
        {
            accepted = await _auth.ValidateAsync(credentials, request.Company, ct);
        }
        catch (EpicorException ex)
        {
            // AuthService already turns InvalidCredentials into accepted=false,
            // so only InvalidApiKey, AccessDenied, or Other reach this catch.
            // None of them are a wrong password and must not be reported as one.
            _logger.LogError(ex,
                "Epicor error ({Reason}) while validating {Username} on {Company}",
                ex.Reason, request.Username, request.Company);

            var message = ex.Reason switch
            {
                EpicorErrorReason.InvalidApiKey =>
                    "La aplicacion no pudo autenticarse con Epicor (clave de API invalida). Avisa a sistemas.",
                EpicorErrorReason.AccessDenied =>
                    "Tu usuario de Epicor no tiene permiso para consultar proveedores. Pide que revisen tu perfil de seguridad en Epicor.",
                _ =>
                    "No se pudo contactar a Epicor. Intenta de nuevo o avisa a sistemas."
            };

            return StatusCode(503, new { message });
        }

        if (!accepted)
        {
            _logger.LogWarning("Rejected login for {Username} on {Company}",
                request.Username, request.Company);
            return Unauthorized(new { message = "Usuario o contrasena incorrectos." });
        }

        var sessionId = _sessions.Create(credentials);
        _sessions.SetContext(sessionId, request.Company, string.Empty, null);

        Response.Cookies.Append(SessionMiddleware.CookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            MaxAge = TimeSpan.FromHours(8)
        });

        _logger.LogInformation("Login succeeded for {Username} on {Company}",
            request.Username, request.Company);

        return Ok(new SessionResponse(
            request.Username, request.Company, string.Empty, null, null));
    }

    /// <summary>
    /// Anonymous: the company must be chosen before authenticating, because the
    /// Epicor URL is company-scoped.
    /// </summary>
    [HttpGet("companies")]
    public IActionResult Companies([FromServices] IOptions<EpicorOptions> options)
        => Ok(options.Value.Companies);

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        if (Request.Cookies.TryGetValue(SessionMiddleware.CookieName, out var sessionId))
        {
            _sessions.Remove(sessionId);
        }

        Response.Cookies.Delete(SessionMiddleware.CookieName);
        return NoContent();
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
        {
            return Unauthorized();
        }

        return Ok(new SessionResponse(
            session.Username, session.Company, session.Plant, session.BuyerId, null));
    }
}
