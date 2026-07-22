using Microsoft.AspNetCore.Mvc;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Vendors;

namespace OCAutomatica.Api.Controllers;

[ApiController]
[Route("api/vendors")]
public sealed class VendorsController : ControllerBase
{
    private readonly IVendorService _vendors;
    private readonly ISessionStore _sessions;
    private readonly ILogger<VendorsController> _logger;

    public VendorsController(
        IVendorService vendors,
        ISessionStore sessions,
        ILogger<VendorsController> logger)
    {
        _vendors = vendors;
        _sessions = sessions;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string search, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(search)) return Ok(Array.Empty<Vendor>());

        try
        {
            var vendors = await _vendors.SearchAsync(session.Company, search, credentials, ct);
            return Ok(vendors);
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while searching vendors for {Username} on {Company}",
                ex.Reason, session.Username, session.Company);
            return HandleEpicorException(ex);
        }
    }

    private IActionResult HandleEpicorException(EpicorException ex)
    {
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
}
