using Microsoft.AspNetCore.Mvc;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Parts;

namespace OCAutomatica.Api.Controllers;

[ApiController]
[Route("api/parts")]
public sealed class PartsController : ControllerBase
{
    private readonly IPartService _parts;
    private readonly ISessionStore _sessions;
    private readonly ILogger<PartsController> _logger;

    public PartsController(
        IPartService parts,
        ISessionStore sessions,
        ILogger<PartsController> logger)
    {
        _parts = parts;
        _sessions = sessions;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetByVendor([FromQuery] string vendorId, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(vendorId))
            return BadRequest(new { message = "Es necesario indicar un proveedor." });

        try
        {
            var parts = await _parts.GetByVendorAsync(
                session.Company, session.Plant, vendorId, credentials, ct);
            return Ok(parts);
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while fetching parts for vendor {VendorId} on {Company}",
                ex.Reason, vendorId, session.Company);
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
                "Tu usuario de Epicor no tiene permiso para consultar este BAQ. Pide que revisen tu perfil de seguridad en Epicor.",
            _ =>
                "No se pudo contactar a Epicor. Intenta de nuevo o avisa a sistemas."
        };

        return StatusCode(503, new { message });
    }
}
