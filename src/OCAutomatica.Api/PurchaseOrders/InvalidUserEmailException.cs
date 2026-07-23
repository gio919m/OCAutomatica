namespace OCAutomatica.Api.PurchaseOrders;

/// <summary>
/// Thrown when the logged-in user's Epicor record has no email, or an
/// invalid one, captured — mirrors the legacy ValidaEmail check in
/// btnEnviarEmail_Click (spec section 2.5).
/// </summary>
public sealed class InvalidUserEmailException : Exception
{
    public InvalidUserEmailException(string message) : base(message)
    {
    }
}
