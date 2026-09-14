using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Resources;

/// <summary>
/// Merchant-facing copy for the "Rejected" outcome. Kept out of the validation rules so the wording can
/// change - or be localised later - without touching the logic, and so tests assert against the same
/// strings the API returns.
/// </summary>
public static class PaymentRejectionMessages
{
    public const string CardNumberInvalid = "Card number must be 14 to 19 digits.";
    public const string ExpiryMonthInvalid = "Expiry month must be between 1 and 12.";
    public const string CardExpired = "Card has expired.";
    public const string AmountInvalid = "Amount must be greater than zero.";
    public const string CvvInvalid = "CVV must be 3 or 4 digits.";

    // Reported when the framework cannot bind the request at all: malformed JSON, an ill-typed value, or
    // an explicit null on a member that cannot be null.
    public const string BodyUnreadable = "The request body could not be read.";

    // Built from the supported list rather than hard-coded, so adding a currency cannot leave this stale.
    public static readonly string CurrencyUnsupported = $"Currency must be one of {string.Join(", ", SupportedCurrencies.Currencies)}.";
}