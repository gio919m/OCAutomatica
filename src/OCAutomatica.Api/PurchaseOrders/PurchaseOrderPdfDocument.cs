using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OCAutomatica.Api.PurchaseOrders;

public sealed class PurchaseOrderPdfDocument : IDocument
{
    private readonly PurchaseOrderReportData _data;

    public PurchaseOrderPdfDocument(PurchaseOrderReportData data) => _data = data;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(30);
            page.Size(PageSizes.Letter);
            page.DefaultTextStyle(x => x.FontSize(9));

            page.Content().Column(column =>
            {
                column.Spacing(10);
                column.Item().Element(ComposeTopBlock); // first page only — nothing else repeats it
                column.Item().Element(ComposeLinesTable);
                column.Item().Element(ComposeTotalsAndComments);
            });

            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeTopBlock(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(6);

            column.Item().Row(row =>
            {
                row.RelativeItem().Text("ORDEN DE COMPRA").Bold().FontSize(14);
                row.ConstantItem(80).AlignRight().Text(_data.Approved ? "APROBADA" : "NO APROBADA").Bold();
            });
            column.Item().Text($"Fecha Impresion: {_data.PrintedAt:dd/MM/yyyy} {_data.PrintedAt:hh:mm}tt").FontSize(8);
            column.Item().Text($"{_data.CompanyName} {_data.PlantName}").Bold();
            column.Item().Text($"Orden de Compra: {_data.PoNum}").Bold();

            column.Item().Row(row =>
            {
                row.RelativeItem().Border(1).Padding(6).Column(box =>
                {
                    box.Item().Text($"Proveedor:  {_data.VendorId}").Bold();
                    box.Item().Text(_data.VendorName);
                    box.Item().Text(_data.VendorAddress1);
                    box.Item().Text(_data.VendorAddress2);
                    box.Item().Text(_data.VendorCity);
                });
                row.RelativeItem().Border(1).Padding(6).Column(box =>
                {
                    box.Item().Text("Domicilio de Entrega:").Bold();
                    box.Item().Text(_data.DeliveryAddress);
                });
            });

            column.Item().Border(1).Padding(6).Row(row =>
            {
                row.RelativeItem().Text($"Entrega: {_data.ShipViaDescription}").Bold();
                row.RelativeItem().Text($"Fecha de Orden: {_data.OrderDate:yyyy-MM-dd}").Bold();
            });
        });
    }

    private void ComposeLinesTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(25);   // Linea
                columns.RelativeColumn(3);    // Parte/Rev/Descripcion
                columns.RelativeColumn(1);    // EAN
                columns.RelativeColumn(1);    // Cantidad
                columns.RelativeColumn(1);    // UOM
                columns.RelativeColumn(1);    // Precio Unit
                columns.RelativeColumn(1);    // Precio Ext
            });

            table.Header(header =>
            {
                header.Cell().Text("Linea").Bold();
                header.Cell().Text("Parte\\Rev\\Descripcion").Bold();
                header.Cell().Text("EAN:").Bold();
                header.Cell().AlignRight().Text("Cantidad").Bold();
                header.Cell().Text("UOM").Bold();
                header.Cell().AlignRight().Text("Precio Unit").Bold();
                header.Cell().AlignRight().Text("Precio Ext").Bold();
                header.Cell().ColumnSpan(7).PaddingTop(2).BorderBottom(1);
            });

            foreach (var line in _data.Lines)
            {
                table.Cell().Text(line.Line.ToString());
                table.Cell().Column(col =>
                {
                    col.Item().Text(line.PartNum);
                    col.Item().Text(line.Description);
                });
                table.Cell().Text(line.Ean);
                table.Cell().AlignRight().Text(line.OrderQty.ToString("0.00"));
                table.Cell().Text(line.Uom);
                table.Cell().AlignRight().Text(line.UnitCost.ToString("0.00"));
                table.Cell().AlignRight().Text(line.ExtendedPrice.ToString("0.00"));
            }
        });
    }

    private void ComposeTotalsAndComments(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(4);

            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text($"Autorizado por: {_data.BuyerName}").Bold();
                    left.Item().Text("Solicitado Por:");
                });
                row.RelativeItem().Column(right =>
                {
                    right.Item().AlignRight().Text($"Subtotal Linea(s): {_data.Subtotal:0.00}");
                    right.Item().AlignRight().Text($"Impuestos: {_data.Taxes:0.00}");
                    right.Item().AlignRight().Text($"Retenciones: {_data.Withholdings:0.00}");
                    right.Item().AlignRight().PaddingTop(4).BorderTop(1).Text($"Total: {_data.Total:0.00}").Bold();
                });
            });

            column.Item().PaddingTop(8).Text("Comentarios").Bold().Underline();
            column.Item().Text(string.IsNullOrWhiteSpace(_data.CommentText) ? " " : _data.CommentText);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        // This is literally the Page Footer section of the original Crystal
        // report — confirmed by the user with a screenshot of the report
        // designer — so it repeats on every page (spec section 2.4 / 3).
        container.Column(column =>
        {
            column.Spacing(2);

            column.Item().Row(row =>
            {
                row.RelativeItem().Text(_data.CompanyName).Bold();
                row.ConstantItem(100).AlignRight().Text(text =>
                {
                    text.Span("Página ");
                    text.CurrentPageNumber();
                    text.Span(" de ");
                    text.TotalPages();
                });
            });
            column.Item().BorderTop(1);

            column.Item().Text("-Direccion de envió de archivos electronicos: recepcion@buzonfiscal.com.").FontSize(7);
            column.Item().Text("-Es necesario que toda factura de orden de compra recibida por CFSJ, se envie al correo antes mencionado para que proceda el pago de la misma.").FontSize(7);
            column.Item().Text("-La presente orden de compra,cancela los pedidos no entregados con anterioridad.").FontSize(7);
            column.Item().Text(text =>
            {
                text.Span("-Favor de confirmar fecha de entrega a ").FontSize(7);
                text.Span(string.IsNullOrWhiteSpace(_data.BuyerEmail) ? string.Empty : $"{_data.BuyerEmail} ,").FontSize(7);
                text.Span("elvira.reyes@carnessanjuan.com.").FontSize(7);
            });
        });
    }
}
