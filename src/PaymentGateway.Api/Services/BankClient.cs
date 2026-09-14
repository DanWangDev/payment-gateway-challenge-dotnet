using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Calls the acquiring bank. Returns null when the bank is unavailable or provides no valid decision.
/// Caller cancellation propagates to the caller.
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

            return bankResponse?.Authorized switch
            {
                true => PaymentStatus.Authorized,
                false => PaymentStatus.Declined,
                _ => null
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    // Model the bank's authorization code, although this exercise does not store or return it.
    private sealed record BankPaymentResponse(
        bool? Authorized,
        [property: JsonPropertyName("authorization_code")] string? AuthorizationCode);
}
