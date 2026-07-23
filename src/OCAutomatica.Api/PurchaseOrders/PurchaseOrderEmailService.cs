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

        var vendorPath = $"Erp.BO.POSvc/POes('{company}',{poNum})?$select=VendorVendorID,VendorName";
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
}

public sealed class PoVendorInfoDto
{
    public string VendorVendorID { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
}
