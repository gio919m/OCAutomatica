using System.Text.RegularExpressions;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Organization;
using OCAutomatica.Api.Users;

namespace OCAutomatica.Api.PurchaseOrders;

public sealed class PurchaseOrderEmailService : IPurchaseOrderEmailService
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly IEpicorClient _epicor;
    private readonly IUserDirectoryService _users;
    private readonly IOrganizationService _organization;
    private readonly IEmailQueueRepository _queue;

    public PurchaseOrderEmailService(
        IEpicorClient epicor,
        IUserDirectoryService users,
        IOrganizationService organization,
        IEmailQueueRepository queue)
    {
        _epicor = epicor;
        _users = users;
        _organization = organization;
        _queue = queue;
    }

    public async Task<string> SendCopyToUserAsync(
        string company,
        string companyName,
        string plant,
        string username,
        int poNum,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var email = await _users.GetEmailAsync(username, credentials, ct);
        if (string.IsNullOrWhiteSpace(email) || !EmailPattern.IsMatch(email))
        {
            throw new InvalidUserEmailException(
                "La direccion de email del usuario no esta capturada correctamente, no se puede enviar el email.");
        }

        var escapedCompany = Uri.EscapeDataString(company.Replace("'", "''"));
        var vendorPath = $"Erp.BO.POSvc/POes('{escapedCompany}',{poNum})?$select=VendorVendorID,VendorName";
        var vendor = await _epicor.GetAsync<PoVendorInfoDto>(company, vendorPath, credentials, ct);

        var plants = await _organization.GetPlantsAsync(company, credentials, ct);
        var plantName = plants.FirstOrDefault(p => p.PlantId == plant)?.Name ?? plant;

        var entry = new EmailQueueEntry(
            company,
            companyName,
            plant,
            plantName,
            poNum.ToString(),
            vendor?.VendorVendorID ?? string.Empty,
            vendor?.VendorName ?? string.Empty,
            email,
            2); // oc_tipo=2: copy to the logged-in buyer (spec section 2.5)

        await _queue.InsertAsync(entry, ct);

        return email;
    }

    public async Task<IReadOnlyList<EmailRecipient>> GetRecipientsAsync(
        string company,
        string vendorId,
        string username,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var recipients = new List<EmailRecipient>();

        var vendorEmails = await GetVendorEmailsAsync(company, vendorId, credentials, ct);
        recipients.AddRange(vendorEmails.Select(e => new EmailRecipient("PROVEEDOR", e, true)));

        var userEmail = await _users.GetEmailAsync(username, credentials, ct);
        recipients.Add(!string.IsNullOrWhiteSpace(userEmail) && EmailPattern.IsMatch(userEmail)
            ? new EmailRecipient("CREADOR", userEmail, true)
            : new EmailRecipient("CREADOR", string.Empty, false));

        return recipients;
    }

    private async Task<List<string>> GetVendorEmailsAsync(
        string company, string vendorId, EpicorCredentials credentials, CancellationToken ct)
    {
        var escaped = Uri.EscapeDataString(vendorId.Replace("'", "''"));
        var path = $"Erp.BO.VendorSvc/Vendors?$filter=VendorID eq '{escaped}'" +
            "&$select=ud_Correo1_c,ud_Correo2_c,ud_Correo3_c&$top=1";
        var response = await _epicor.GetAsync<VendorEmailsListResponse>(company, path, credentials, ct);
        var dto = response?.Value.FirstOrDefault();
        if (dto is null) return new List<string>();

        return new[] { dto.ud_Correo1_c, dto.ud_Correo2_c, dto.ud_Correo3_c }
            .Where(e => !string.IsNullOrWhiteSpace(e) && EmailPattern.IsMatch(e))
            .Distinct()
            .ToList();
    }
}

public sealed class PoVendorInfoDto
{
    public string VendorVendorID { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
}

public sealed class VendorEmailsListResponse
{
    public List<VendorEmailsDto> Value { get; set; } = new();
}

public sealed class VendorEmailsDto
{
    // Raw SQL column names on Vendor_UD, confirmed exposed directly on the
    // Vendor entity by Epicor's OData layer (same pattern as other "_c"
    // fields already confirmed on the PO header in Plan 4) — verify live
    // against Swagger before Task 2's live-verification step.
    public string ud_Correo1_c { get; set; } = string.Empty;
    public string ud_Correo2_c { get; set; } = string.Empty;
    public string ud_Correo3_c { get; set; } = string.Empty;
}
