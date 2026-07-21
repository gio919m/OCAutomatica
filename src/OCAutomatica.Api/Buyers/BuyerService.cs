using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Buyers;

public sealed class BuyerService : IBuyerService
{
    private const string BuyersPath = "Erp.BO.PurAgentSvc/PurAgents?$expand=PurAuths";

    private readonly IEpicorClient _epicor;

    public BuyerService(IEpicorClient epicor) => _epicor = epicor;

    public async Task<Buyer?> ResolveDefaultBuyerAsync(
        string company,
        string username,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var response = await _epicor.GetAsync<BuyerListResponse>(
            company, BuyersPath, credentials, ct);

        if (response is null) return null;

        var match = response.Value.FirstOrDefault(buyer =>
            buyer.PurAuths.Any(auth =>
                auth.IsPrimaryUser &&
                string.Equals(auth.DcdUserID, username, StringComparison.OrdinalIgnoreCase)));

        return match is null ? null : new Buyer(match.BuyerID, match.Name);
    }
}
