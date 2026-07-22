namespace OCAutomatica.Api.Vendors;

public sealed record Vendor(string VendorId, string Name);

public sealed class VendorListResponse
{
    public List<VendorDto> Value { get; set; } = new();
}

public sealed class VendorDto
{
    public string VendorID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
