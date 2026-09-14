using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Resources;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Validates payment request fields before bank processing.
/// </summary>
public static class PaymentRequestValidator
{
    /// <summary>
    /// Returns one message per failed rule, in a stable order. An empty list means the request is valid.
    /// </summary>
    /// <param name="today">
    /// The caller supplies the date so validation is independent of the system clock.
    /// </param>
    public static List<string> Validate(PostPaymentRequest request, DateOnly today)
    {
        var errors = new List<string>();

        if (!HasAsciiDigits(request.CardNumber, 14, 19))
        {
            errors.Add(PaymentRejectionMessages.CardNumberInvalid);
        }

        var expiryError = ValidateExpiry(request.ExpiryMonth, request.ExpiryYear, today);
        if (expiryError is not null)
        {
            errors.Add(expiryError);
        }

        if (!SupportedCurrencies.Currencies.Contains(request.Currency))
        {
            errors.Add(PaymentRejectionMessages.CurrencyUnsupported);
        }

        if (request.Amount <= 0)
        {
            errors.Add(PaymentRejectionMessages.AmountInvalid);
        }

        if (!HasAsciiDigits(request.Cvv, 3, 4))
        {
            errors.Add(PaymentRejectionMessages.CvvInvalid);
        }

        return errors;
    }

    private static bool HasAsciiDigits(string? value, int minLength, int maxLength)
    {
        return value is not null
            && value.Length >= minLength
            && value.Length <= maxLength
            && value.All(char.IsAsciiDigit);
    }

    private static string? ValidateExpiry(int month, int year, DateOnly today)
    {
        if (month is < 1 or > 12)
        {
            return PaymentRejectionMessages.ExpiryMonthInvalid;
        }

        if (HasExpired(month, year, today))
        {
            return PaymentRejectionMessages.CardExpired;
        }

        return null;
    }

    // Cards remain valid through the end of their expiry month.
    private static bool HasExpired(int month, int year, DateOnly today)
    {
        return year < today.Year || (year == today.Year && month < today.Month);
    }
}
