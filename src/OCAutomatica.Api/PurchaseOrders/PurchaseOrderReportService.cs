using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Users;

namespace OCAutomatica.Api.PurchaseOrders;

public sealed class PurchaseOrderReportService : IPurchaseOrderReportService
{
    private readonly IEpicorClient _epicor;
    private readonly IUserDirectoryService _users;
    private readonly TimeProvider _time;

    public PurchaseOrderReportService(IEpicorClient epicor, IUserDirectoryService users, TimeProvider time)
    {
        _epicor = epicor;
        _users = users;
        _time = time;
    }

    public async Task<PurchaseOrderReportData?> GetReportDataAsync(
        string company,
        string companyName,
        string plant,
        int poNum,
        string username,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var escapedCompany = Uri.EscapeDataString(company.Replace("'", "''"));
        var headerPath = $"Erp.BO.POSvc/POes('{escapedCompany}',{poNum})?$expand=PODetails";
        var header = await _epicor.GetAsync<PurchaseOrderReportHeaderDto>(company, headerPath, credentials, ct);
        if (header is null || header.PONum == 0) return null;

        var vendorTask = GetVendorAddressAsync(company, header.VendorVendorID, credentials, ct);
        var companyTask = GetCompanyAddressAsync(company, credentials, ct);
        var plantTask = GetPlantDetailsAsync(company, plant, credentials, ct);
        var shipViaTask = string.IsNullOrWhiteSpace(header.ShipViaCode)
            ? Task.FromResult<ShipViaDto?>(null)
            : GetShipViaAsync(company, header.ShipViaCode, credentials, ct);
        var eanTask = GetEanCodesAsync(company, header.PODetails, credentials, ct);
        var emailTask = _users.GetEmailAsync(username, credentials, ct);

        await Task.WhenAll(vendorTask, companyTask, plantTask, shipViaTask, eanTask, emailTask);

        var vendor = vendorTask.Result;
        var companyAddr = companyTask.Result;
        var plantDetails = plantTask.Result;
        var shipVia = shipViaTask.Result;
        var eanByPart = eanTask.Result;

        var deliveryAddress = BuildDeliveryAddress(header, companyAddr, plantDetails?.PhoneNum ?? string.Empty);

        var lines = header.PODetails
            .OrderBy(d => d.POLine)
            .Select(d => new PurchaseOrderReportLine(
                d.POLine,
                d.PartNum,
                d.LineDesc,
                eanByPart.GetValueOrDefault((d.PartNum, d.IUM), string.Empty),
                d.OrderQty,
                d.IUM,
                d.UnitCost,
                d.OrderQty * d.UnitCost))
            .ToList();

        var subtotal = lines.Sum(l => l.ExtendedPrice);

        return new PurchaseOrderReportData(
            header.PONum,
            companyName,
            plantDetails?.Name ?? string.Empty,
            header.Approve,
            header.OrderDate,
            _time.GetUtcNow(),
            header.VendorVendorID,
            header.VendorName,
            vendor?.Address1 ?? string.Empty,
            vendor?.Address2 ?? string.Empty,
            vendor?.City ?? string.Empty,
            deliveryAddress,
            shipVia?.Description ?? string.Empty,
            header.BuyerIDName,
            header.CommentText,
            emailTask.Result ?? string.Empty,
            lines,
            subtotal,
            header.DocTotalTax,
            header.TotalWhTax,
            subtotal + header.DocTotalTax - header.TotalWhTax);
    }

    private async Task<VendorAddressDto?> GetVendorAddressAsync(
        string company, string vendorId, EpicorCredentials credentials, CancellationToken ct)
    {
        var escaped = Uri.EscapeDataString(vendorId.Replace("'", "''"));
        var path = $"Erp.BO.VendorSvc/Vendors?$filter=VendorID eq '{escaped}'&$select=Address1,Address2,City&$top=1";
        var response = await _epicor.GetAsync<VendorAddressListResponse>(company, path, credentials, ct);
        return response?.Value.FirstOrDefault();
    }

    private async Task<CompanyAddressDto?> GetCompanyAddressAsync(
        string company, EpicorCredentials credentials, CancellationToken ct)
    {
        var escaped = Uri.EscapeDataString(company.Replace("'", "''"));
        // Epicor exposes this key as "Company1" (same convention as Plant1 on
        // Erp.Plant) — confirmed live via Swagger's keyed route
        // Companies('{Company1}'), not "Company" as the raw SQL column is named.
        var path = $"Erp.BO.CompanySvc/Companies?$filter=Company1 eq '{escaped}'&$select=Address1,Address2,City,State,Zip,StateTaxID&$top=1";
        var response = await _epicor.GetAsync<CompanyAddressListResponse>(company, path, credentials, ct);
        return response?.Value.FirstOrDefault();
    }

    private async Task<PlantDetailsDto?> GetPlantDetailsAsync(
        string company, string plant, EpicorCredentials credentials, CancellationToken ct)
    {
        var escaped = Uri.EscapeDataString(plant.Replace("'", "''"));
        var path = $"Erp.BO.PlantSvc/Plants?$filter=Plant1 eq '{escaped}'&$select=Name,PhoneNum&$top=1";
        var response = await _epicor.GetAsync<PlantDetailsListResponse>(company, path, credentials, ct);
        return response?.Value.FirstOrDefault();
    }

    private async Task<ShipViaDto?> GetShipViaAsync(
        string company, string shipViaCode, EpicorCredentials credentials, CancellationToken ct)
    {
        var escaped = Uri.EscapeDataString(shipViaCode.Replace("'", "''"));
        var path = $"Erp.BO.ShipViaSvc/ShipVias?$filter=ShipViaCode eq '{escaped}'&$select=Description&$top=1";
        var response = await _epicor.GetAsync<ShipViaListResponse>(company, path, credentials, ct);
        return response?.Value.FirstOrDefault();
    }

    private async Task<Dictionary<(string PartNum, string Uom), string>> GetEanCodesAsync(
        string company, List<PODetailDto> lines, EpicorCredentials credentials, CancellationToken ct)
    {
        if (lines.Count == 0) return new();

        var pairs = lines
            .Select(l => (l.PartNum, l.IUM))
            .Distinct()
            .ToList();

        var filterClauses = pairs.Select(p =>
        {
            var part = Uri.EscapeDataString(p.PartNum.Replace("'", "''"));
            var uom = Uri.EscapeDataString(p.IUM.Replace("'", "''"));
            return $"(PartNum eq '{part}' and UOMCode eq '{uom}')";
        });

        var path = $"Erp.BO.PartSvc/PartPCs?$filter=PCType eq 'EAN-13' and ({string.Join(" or ", filterClauses)})" +
            "&$select=PartNum,UOMCode,PRODCODE";
        var response = await _epicor.GetAsync<PartEanListResponse>(company, path, credentials, ct);

        return (response?.Value ?? new List<PartEanDto>())
            .ToDictionary(e => (e.PartNum, e.UOMCode), e => e.PRODCODE);
    }

    private static string BuildDeliveryAddress(
        PurchaseOrderReportHeaderDto header, CompanyAddressDto? company, string plantPhone)
    {
        // Mirrors the legacy Crystal report's CASE formula exactly (spec
        // section 2.1): fall back to the Company's own address only when
        // the PO's ShipAddress fields are blank.
        var address1 = string.IsNullOrEmpty(header.ShipAddress1) ? company?.Address1 ?? string.Empty : header.ShipAddress1;
        var address2 = string.IsNullOrEmpty(header.ShipAddress2) ? company?.Address2 ?? string.Empty : header.ShipAddress2;
        var city = string.IsNullOrEmpty(header.ShipCity) ? company?.City ?? string.Empty : header.ShipCity;
        var state = string.IsNullOrEmpty(header.ShipState) ? company?.State ?? string.Empty : header.ShipState;
        var zip = string.IsNullOrEmpty(header.ShipZIP) ? company?.Zip ?? string.Empty : header.ShipZIP;

        return $"{address1}  {address2}  {city} {state} CP:{zip} TEL:{plantPhone} RFC: {company?.StateTaxID ?? string.Empty}";
    }
}
