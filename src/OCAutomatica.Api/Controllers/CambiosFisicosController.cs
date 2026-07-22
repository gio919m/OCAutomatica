using Microsoft.AspNetCore.Mvc;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.PurchaseOrders;

namespace OCAutomatica.Api.Controllers;

[ApiController]
[Route("api/cambios-fisicos")]
public sealed class CambiosFisicosController : ControllerBase
{
    private readonly ICambiosFisicosService _cambiosFisicos;
    private readonly ISessionStore _sessions;
    private readonly ILogger<CambiosFisicosController> _logger;

    public CambiosFisicosController(
        ICambiosFisicosService cambiosFisicos,
        ISessionStore sessions,
        ILogger<CambiosFisicosController> logger)
    {
        _cambiosFisicos = cambiosFisicos;
        _sessions = sessions;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetPending([FromQuery] string vendorId, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        // Not an error: the buyer simply hasn't picked a vendor yet.
        if (string.IsNullOrWhiteSpace(vendorId))
            return Ok(Array.Empty<CambioFisico>());

        try
        {
            var cambiosFisicos = await _cambiosFisicos.GetPendingAsync(
                session.Company, session.Plant, vendorId, credentials, ct);
            return Ok(cambiosFisicos);
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while fetching cambios fisicos for vendor {VendorId} on {Company}",
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
                "Tu usuario de Epicor no tiene permiso para consultar Cambios Fisicos. Pide que revisen tu perfil de seguridad en Epicor.",
            _ =>
                "No se pudo contactar a Epicor. Intenta de nuevo o avisa a sistemas."
        };

        return StatusCode(503, new { message });
    }
}
