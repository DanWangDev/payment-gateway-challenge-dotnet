using System.Net.Http.Json;
using System.Text.Json;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Calls the acquiring bank. A null result means the bank gave no answer - unreachable, error status, or
/// an unreadable body - which the caller reports as 502 rather than as a decline.
/// </summary>
public class BankClient
{
    private readonly HttpClient _httpClient;

    public BankClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PaymentStatus?> AuthorizeAsync(PostPaymentRequest request, CancellationToken cancellationToken)
    {
        var body = new
        {
            card_number = request.CardNumber,
            expiry_date = $"{request.ExpiryMonth:00}/{request.ExpiryYear}",
            currency = request.Currency,
            amount = request.Amount,
            cvv = request.Cvv
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("payments", body, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bankResponse = await response.Content.ReadFromJsonAsync<BankPaymentResponse>(cancellationToken);

            if (bankResponse is null)
            {
                return null;
            }

            return bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return null;
        }
    }

    private sealed record BankPaymentResponse(bool Authorized);
}
