using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

public interface IPurchaseOrderEmailService
{
    /// <returns>The email address the copy was sent to.</returns>
    /// <exception cref="InvalidUserEmailException">
    /// The logged-in user has no valid email captured in Epicor.
    /// </exception>
    Task<string> SendCopyToUserAsync(
        string company,
        string companyName,
        string plant,
        string username,
        int poNum,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
