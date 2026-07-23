using Microsoft.AspNetCore.Mvc;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.PurchaseOrders;
using QuestPDF.Fluent;

namespace OCAutomatica.Api.Controllers;

[ApiController]
[Route("api/purchase-orders")]
public sealed class PurchaseOrdersController : ControllerBase
{
    private readonly IPurchaseOrderService _purchaseOrders;
    private readonly IPurchaseOrderHistoryService _history;
    private readonly IPurchaseOrderReportService _reports;
    private readonly IPurchaseOrderEmailService _emailService;
    private readonly ISessionStore _sessions;
    private readonly ILogger<PurchaseOrdersController> _logger;

    public PurchaseOrdersController(
        IPurchaseOrderService purchaseOrders,
        IPurchaseOrderHistoryService history,
        IPurchaseOrderReportService reports,
        IPurchaseOrderEmailService emailService,
        ISessionStore sessions,
        ILogger<PurchaseOrdersController> logger)
    {
        _purchaseOrders = purchaseOrders;
        _history = history;
        _reports = reports;
        _emailService = emailService;
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

    [HttpGet]
    public async Task<IActionResult> GetByVendor(
        [FromQuery] string vendorId, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(vendorId))
            return BadRequest(new { message = "Es necesario indicar un proveedor." });

        try
        {
            var orders = await _history.GetByVendorAsync(session.Company, vendorId, credentials, ct);
            return Ok(orders);
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while listing purchase orders for vendor {VendorId} on {Company}",
                ex.Reason, vendorId, session.Company);
            return HandleEpicorException(ex);
        }
    }

    [HttpGet("{poNum:int}/lines")]
    public async Task<IActionResult> GetLines(int poNum, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        try
        {
            var lines = await _history.GetDetailAsync(session.Company, poNum, credentials, ct);
            return Ok(lines);
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while fetching lines for PO {PoNum} on {Company}",
                ex.Reason, poNum, session.Company);
            return HandleEpicorException(ex);
        }
    }

    [HttpGet("{poNum:int}/report")]
    public async Task<IActionResult> GetReport(int poNum, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        var companyName = session.AvailableCompanies
            .FirstOrDefault(c => c.Company == session.Company)?.CompanyName ?? session.Company;

        try
        {
            var data = await _reports.GetReportDataAsync(
                session.Company, companyName, session.Plant, poNum, session.Username, credentials, ct);

            if (data is null)
                return NotFound(new { message = "No se encontro la orden de compra." });

            var bytes = new PurchaseOrderPdfDocument(data).GeneratePdf();
            return File(bytes, "application/pdf");
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while building the report for PO {PoNum} on {Company}",
                ex.Reason, poNum, session.Company);
            return HandleEpicorException(ex);
        }
    }

    [HttpPost("{poNum:int}/send-copy")]
    public async Task<IActionResult> SendCopy(int poNum, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        var companyName = session.AvailableCompanies
            .FirstOrDefault(c => c.Company == session.Company)?.CompanyName ?? session.Company;

        try
        {
            var email = await _emailService.SendCopyToUserAsync(
                session.Company, companyName, session.Plant, session.Username, poNum, credentials, ct);

            return Ok(new { message = $"Se envio la OC a tu correo: {email}" });
        }
        catch (InvalidUserEmailException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while sending a PO copy email for PO {PoNum} on {Company}",
                ex.Reason, poNum, session.Company);
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
