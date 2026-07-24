using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

/// <summary>
/// One row of the "Correos a los que se enviará la OC" grid. Tipo is
/// "PROVEEDOR" or "CREADOR" — the frontend adds "INCLUIR" rows locally for
/// manually-typed emails, which never come from this method.
/// </summary>
public sealed record EmailRecipient(string Tipo, string Email, bool Valido);

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

    /// <summary>
    /// Builds the PROVEEDOR + CREADOR rows for the recipients grid. Does
    /// not send anything — read-only, used to populate the UI.
    /// </summary>
    Task<IReadOnlyList<EmailRecipient>> GetRecipientsAsync(
        string company,
        string vendorId,
        string username,
        EpicorCredentials credentials,
        CancellationToken ct = default);

    /// <summary>
    /// Recomputes the recipients from scratch (never trusts what the
    /// browser last fetched), merges in any manually-added emails, and
    /// queues the send with oc_tipo=1. Never throws for an empty or
    /// all-invalid recipient list — mirrors the legacy, which always
    /// queues the send regardless (a missing vendor email is only an
    /// informational warning there, never a block).
    /// </summary>
    Task SendToVendorAsync(
        string company,
        string companyName,
        string plant,
        string vendorId,
        string username,
        int poNum,
        IReadOnlyList<string> manualEmails,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
