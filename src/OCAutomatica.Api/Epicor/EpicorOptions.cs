namespace OCAutomatica.Api.Epicor;

public sealed class EpicorOptions
{
    public const string SectionName = "Epicor";

    /// <summary>Base URL without trailing slash, e.g. https://server/erp102600v2</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Any company known to exist on the server. Used only to anchor the URL
    /// for company-agnostic calls (e.g. looking up which companies a user can
    /// access) — Epicor's REST surface requires a company segment in every
    /// URL even when the underlying table (like Ice.UserComp) is system-wide.
    /// </summary>
    public string AnchorCompany { get; set; } = string.Empty;
}
