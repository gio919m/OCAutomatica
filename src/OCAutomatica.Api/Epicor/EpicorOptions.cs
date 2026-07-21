namespace OCAutomatica.Api.Epicor;

public sealed class EpicorOptions
{
    public const string SectionName = "Epicor";

    /// <summary>Base URL without trailing slash, e.g. https://server/erp102600v2</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public List<string> Companies { get; set; } = new();
}
