using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

public interface IPurchaseOrderReportService
{
    Task<PurchaseOrderReportData?> GetReportDataAsync(
        string company,
        string companyName,
        string plant,
        int poNum,
        string username,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
