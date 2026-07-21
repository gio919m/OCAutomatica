using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Buyers;

public interface IBuyerService
{
    /// <summary>
    /// Returns the buyer the user owns (where they are marked as Default Buyer),
    /// or null when the user owns none. Being listed as an authorized user is not
    /// enough — that only grants editing rights over someone else's orders.
    /// </summary>
    Task<Buyer?> ResolveDefaultBuyerAsync(
        string company,
        string username,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
