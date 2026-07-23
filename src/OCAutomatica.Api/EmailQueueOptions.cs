namespace OCAutomatica.Api;

public sealed class EmailQueueOptions
{
    public const string SectionName = "EmailQueue";

    public string ConnectionString { get; set; } = string.Empty;
}
