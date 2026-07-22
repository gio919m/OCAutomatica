using Microsoft.AspNetCore.Mvc;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.PurchaseOrders;

namespace OCAutomatica.Api.Controllers;

[ApiController]
[Route("api/purchase-orders")]
public sealed class PurchaseOrdersController : ControllerBase
{
    private readonly IPurchaseOrderService _purchaseOrders;
    private readonly ISessionStore _sessions;
    private readonly ILogger<PurchaseOrdersController> _logger;

    public PurchaseOrdersController(
        IPurchaseOrderService purchaseOrders,
        ISessionStore sessions,
        ILogger<PurchaseOrdersController> logger)
    {
        _purchaseOrders = purchaseOrders;
        _sessions = sessions;
        _logger = logger;
    }

    public sealed record CreatePurchaseOrderApiRequest(
        string VendorId, string? Comentarios, List<PurchaseOrderLine>? Lineas);

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreatePurchaseOrderApiRequest request, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(session.BuyerId))
        {
            return StatusCode(403, new
            {
                message = "No tienes un comprador asignado en esta compania. No puedes crear ordenes de compra."
            });
        }

        if (string.IsNullOrWhiteSpace(request.VendorId))
            return BadRequest(new { message = "Es necesario indicar un proveedor." });

        if (request.Lineas is null || request.Lineas.Count == 0)
            return BadRequest(new { message = "Es necesario marcar al menos un articulo con cantidad a surtir." });

        try
        {
            var result = await _purchaseOrders.CreateAsync(
                session.Company,
                session.Plant,
                session.BuyerId,
                new CreatePurchaseOrderRequest(request.VendorId, request.Comentarios ?? string.Empty, request.Lineas),
                credentials,
                ct);

            return Ok(new { poNum = result.PoNum });
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while creating a purchase order for vendor {VendorId} on {Company}",
                ex.Reason, request.VendorId, session.Company);
            return HandleEpicorException(ex);
        }
    }

    private IActionResult HandleEpicorException(EpicorException ex)
    {
        switch (ex.Reason)
        {
            case EpicorErrorReason.InvalidApiKey:
                return StatusCode(503, new
                {
                    message = "La aplicacion no pudo autenticarse con Epicor (clave de API invalida). Avisa a sistemas."
                });
            case EpicorErrorReason.AccessDenied:
                return StatusCode(503, new
                {
                    message = "Tu usuario de Epicor no tiene permiso para crear ordenes de compra. Pide que revisen tu perfil de seguridad en Epicor."
                });
            default:
                // "Other" covers both real infrastructure failures and
                // OCACrearOC's own business-rule rejections (blocked part,
                // invalid line, etc.). The spec requires the buyer see the
                // actual descriptive error from the Function here, not a
                // generic message, so the error's own text is passed through.
                return StatusCode(422, new { message = ex.Message });
        }
    }
}
