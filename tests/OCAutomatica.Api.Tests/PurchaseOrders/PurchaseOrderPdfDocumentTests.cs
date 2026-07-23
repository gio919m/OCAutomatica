using OCAutomatica.Api.PurchaseOrders;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace OCAutomatica.Api.Tests.PurchaseOrders;

public class PurchaseOrderPdfDocumentTests
{
    static PurchaseOrderPdfDocumentTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static PurchaseOrderReportData BuildSampleData(int lineCount = 1) => new(
        PoNum: 3431,
        PlantName: "LA FE",
        Approved: true,
        OrderDate: new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero),
        PrintedAt: new DateTimeOffset(2026, 7, 23, 13, 39, 34, TimeSpan.Zero),
        VendorId: "001008",
        VendorName: "JARAMILLO TREVIÑO GERARDO MAGDALENO",
        VendorAddress1: "AV LOS ANGELES",
        VendorAddress2: "1000",
        VendorCity: "SAN NICOLAS DE LOS GARZA",
        DeliveryAddress: "AVE. ACAPULCO NUM. 1100  Col. RESIDENCIAL SANTA FE  GUADALUPE NUEVO LEON CP:67112 TEL: RFC: CFS051213DG5",
        ShipViaDescription: "LOCAL",
        BuyerName: "Janneth Yañez",
        CommentText: "CALABAZA GRANDE",
        BuyerEmail: "giovanni.montoya@carnessanjuan.com",
        Lines: Enumerable.Range(1, lineCount)
            .Select(i => new PurchaseOrderReportLine(i, $"PART{i}", $"Descripcion {i}", "12", 4m, "KGS", 14m, 56m))
            .ToList(),
        Subtotal: 56m * lineCount,
        Taxes: 0m,
        Withholdings: 0m,
        Total: 56m * lineCount);

    [Fact]
    public void GeneratePdf_ProducesAValidPdf()
    {
        var document = new PurchaseOrderPdfDocument(BuildSampleData());

        var bytes = document.GeneratePdf();

        Assert.NotEmpty(bytes);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void GeneratePdf_HandlesManyLines_WithoutThrowing()
    {
        // Forces the table to flow across multiple pages — the scenario the
        // header/footer split (Task 5) exists to handle correctly.
        var document = new PurchaseOrderPdfDocument(BuildSampleData(lineCount: 60));

        var bytes = document.GeneratePdf();

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void GeneratePdf_HandlesEmptyOptionalFields_WithoutThrowing()
    {
        var data = BuildSampleData() with
        {
            CommentText = string.Empty,
            ShipViaDescription = string.Empty,
            BuyerEmail = string.Empty,
        };
        var document = new PurchaseOrderPdfDocument(data);

        var bytes = document.GeneratePdf();

        Assert.NotEmpty(bytes);
    }
}
