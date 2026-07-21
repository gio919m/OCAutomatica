using Microsoft.AspNetCore.Mvc;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Buyers;
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

        var plants = await _organization.GetPlantsAsync(session.Company, credentials, ct);
        return Ok(plants);
    }

    [HttpPost("context")]
    public async Task<IActionResult> SetContext(SetContextRequest request, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        var buyer = await _buyers.ResolveDefaultBuyerAsync(
            session.Company, session.Username, credentials, ct);

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
}
