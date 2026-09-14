namespace PaymentGateway.Api.Models.Requests;

public class PostPaymentRequest
{
    /// <summary>Full card number: 14 to 19 ASCII digits. Only the last four are stored or returned.</summary>
    /// <example>1234567890123451</example>
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Expiry month from 1 to 12. The card remains valid through the end of that month.</summary>
    /// <example>12</example>
    public int ExpiryMonth { get; set; }

    /// <summary>Expiry year. The month/year pair must not be in the past, using the current UTC date.</summary>
    /// <example>2099</example>
    public int ExpiryYear { get; set; }

    /// <summary>GBP, USD or EUR. Whitespace is trimmed and letters are upper-cased before validation.</summary>
    /// <example>GBP</example>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Positive integer in minor units, up to 2147483647. For GBP, 1050 means 10.50 pounds.</summary>
    /// <example>1050</example>
    public int Amount { get; set; }

    /// <summary>Three or four ASCII digits, preserving leading zeros. Never stored or returned.</summary>
    /// <example>012</example>
    public string Cvv { get; set; } = string.Empty;
}
