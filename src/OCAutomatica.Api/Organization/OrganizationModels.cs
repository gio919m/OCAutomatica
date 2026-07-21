namespace OCAutomatica.Api.Organization;

public sealed record Plant(string PlantId, string Name);

public sealed class PlantListResponse
{
    public List<PlantDto> Value { get; set; } = new();
}

public sealed class PlantDto
{
    /// <summary>Epicor exposes the Plant key as "Plant1" in the OData payload.</summary>
    public string Plant1 { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
