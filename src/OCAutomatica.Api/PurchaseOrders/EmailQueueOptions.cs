namespace OCAutomatica.Api.PurchaseOrders;

public sealed class EmailQueueOptions
{
    public const string SectionName = "EmailQueue";

    /// <summary>
    /// Connection string to the CFSJService database on 192.168.100.18 —
    /// the same server sp_EnviaOC_V2 inserts into via linked server. This
    /// app connects to it directly and never touches the Epicor SQL Server
    /// (SRVCSJPR2) or the stored procedure itself (see spec section 2.4).
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
