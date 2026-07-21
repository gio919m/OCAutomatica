namespace OCAutomatica.Api.Buyers;

/// <summary>Buyer as exposed to the rest of the application.</summary>
public sealed record Buyer(string BuyerId, string Name);

// DTOs matching the Erp.BO.PurAgentSvc payload, confirmed against the test
// environment (see Task 5, Step 1). PurAgent is the underlying table for
// Buyer Maintenance; PurAuth is its "Authorized Users" child table.

public sealed class BuyerListResponse
{
    public List<BuyerDto> Value { get; set; } = new();
}

public sealed class BuyerDto
{
    public string BuyerID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<PurAuthDto> PurAuths { get; set; } = new();
}

public sealed class PurAuthDto
{
    public string DcdUserID { get; set; } = string.Empty;

    /// <summary>Backs the "Default Buyer" checkbox in Buyer Maintenance's Authorized Users tab.</summary>
    public bool IsPrimaryUser { get; set; }
}
