namespace PaymentGateway.Api.Services;

/// <summary>
/// The ISO 4217 codes this gateway accepts. The challenge requires at most three, and both the validation
/// rule and the rejection copy read from here so that adding a currency cannot leave the message stale.
/// </summary>
public static class SupportedCurrencies
{
    public static readonly string[] Currencies = ["GBP", "USD", "EUR"];
}
