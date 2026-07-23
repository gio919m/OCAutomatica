namespace OCAutomatica.Api.Auth;

/// <summary>A company the logged-in user is allowed to work in.</summary>
public sealed record CompanyAccess(string Company, string CompanyName);

public sealed class UserCompListResponse
{
    public List<UserCompDto> Value { get; set; } = new();
}

public sealed class UserCompDto
{
    public string Company { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
}
