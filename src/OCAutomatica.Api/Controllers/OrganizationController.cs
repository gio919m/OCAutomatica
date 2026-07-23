using Microsoft.AspNetCore.Mvc;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Buyers;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Organization;

namespace OCAutomatica.Api.Controllers;

[ApiController]
[Route("api/organization")]
public sealed class OrganizationController : ControllerBase
{
    public sealed record SetContextRequest(string Plant);
    public sealed record ContextResponse(
        string Company, string Plant, string? BuyerId, string? BuyerName, bool CanCreateOrders);

    private readonly IOrganizationService _organization;
    private readonly IBuyerService _buyers;
    private readonly ISessionStore _sessions;
    private readonly ILogger<OrganizationController> _logger;

    public OrganizationController(
        IOrganizationService organization,
        IBuyerService buyers,
        ISessionStore sessions,
        ILogger<OrganizationController> logger)
    {
        _organization = organization;
        _buyers = buyers;
        _sessions = sessions;
        _logger = logger;
    }

    [HttpGet("plants")]
    public async Task<IActionResult> GetPlants(CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        try
        {
            var plants = await _organization.GetPlantsAsync(session.Company, credentials, ct);
            return Ok(plants);
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while fetching plants for {Username} on {Company}",
                ex.Reason, session.Username, session.Company);
            return HandleEpicorException(ex);
        }
    }

    [HttpPost("context")]
    public async Task<IActionResult> SetContext(SetContextRequest request, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        // Plant (and the buyer it resolves) is fixed for the lifetime of the
        // session — every purchase order created afterwards is stamped with
        // it. Changing to a DIFFERENT plant mid-session (without a fresh
        // login) must never be possible. Re-submitting the SAME plant is a
        // harmless no-op — it must succeed, since a page reload always
        // re-renders the plant picker with no memory of the earlier choice.
        if (!string.IsNullOrEmpty(session.Plant) &&
            !string.Equals(session.Plant, request.Plant, StringComparison.Ordinal))
        {
            return Conflict(new
            {
                message = "La planta ya esta fijada para esta sesion. Cierra sesion para cambiar de planta."
            });
        }

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        Buyer? buyer;
        try
        {
            buyer = await _buyers.ResolveDefaultBuyerAsync(
                session.Company, session.Username, credentials, ct);
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while resolving default buyer for {Username} on {Company}",
                ex.Reason, session.Username, session.Company);
            return HandleEpicorException(ex);
        }

        _sessions.SetContext(session.SessionId, session.Company, request.Plant, buyer?.BuyerId);

        if (buyer is null)
        {
            _logger.LogWarning(
                "User {Username} has no default buyer on {Company}; order creation blocked",
                session.Username, session.Company);
        }

        return Ok(new ContextResponse(
            session.Company,
            request.Plant,
            buyer?.BuyerId,
            buyer?.Name,
            CanCreateOrders: buyer is not null));
    }

    private IActionResult HandleEpicorException(EpicorException ex)
    {
        var message = ex.Reason switch
        {
            EpicorErrorReason.InvalidApiKey =>
                "La aplicacion no pudo autenticarse con Epicor (clave de API invalida). Avisa a sistemas.",
            EpicorErrorReason.AccessDenied =>
                "Tu usuario de Epicor no tiene permiso para consultar esta informacion. Pide que revisen tu perfil de seguridad en Epicor.",
            _ =>
                "No se pudo contactar a Epicor. Intenta de nuevo o avisa a sistemas."
        };

        return StatusCode(503, new { message });
    }
}
