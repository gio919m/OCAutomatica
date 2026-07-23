using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace OCAutomatica.Api.PurchaseOrders;

public sealed class EmailQueueRepository : IEmailQueueRepository
{
    // Column sizes match sp_EnviaOC_V2's own parameter declarations exactly
    // (confirmed against the real stored procedure — see spec section 2.4).
    private const string InsertSql = """
        INSERT INTO dbo.interfaz_envia_oc
            (company, companyName, plant, plantName, poNumber, vendorID, vendorName,
             emails, fechaInserto, fechaUltimaRevision, fechaProceso, estatus, mensaje, oc_tipo)
        VALUES
            (@company, @companyName, @plant, @plantName, @poNumber, @vendorID, @vendorName,
             @emails, GETDATE(), NULL, NULL, 0, '', @oc_tipo)
        """;

    private readonly string _connectionString;

    public EmailQueueRepository(IOptions<EmailQueueOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task InsertAsync(EmailQueueEntry entry, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(InsertSql, connection);
        command.Parameters.Add(new SqlParameter("@company", SqlDbType.VarChar, 20) { Value = entry.Company });
        command.Parameters.Add(new SqlParameter("@companyName", SqlDbType.VarChar, 500) { Value = entry.CompanyName });
        command.Parameters.Add(new SqlParameter("@plant", SqlDbType.VarChar, 20) { Value = entry.Plant });
        command.Parameters.Add(new SqlParameter("@plantName", SqlDbType.VarChar, 500) { Value = entry.PlantName });
        command.Parameters.Add(new SqlParameter("@poNumber", SqlDbType.VarChar, 50) { Value = entry.PoNumber });
        command.Parameters.Add(new SqlParameter("@vendorID", SqlDbType.VarChar, 50) { Value = entry.VendorId });
        command.Parameters.Add(new SqlParameter("@vendorName", SqlDbType.VarChar, 500) { Value = entry.VendorName });
        command.Parameters.Add(new SqlParameter("@emails", SqlDbType.VarChar, -1) { Value = entry.Emails }); // -1 = varchar(max)
        command.Parameters.Add(new SqlParameter("@oc_tipo", SqlDbType.Int) { Value = entry.OcTipo });

        await command.ExecuteNonQueryAsync(ct);
    }
}
