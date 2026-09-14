using System.Diagnostics;
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
/// <remarks>
/// HTTP errors, transport failures and invalid responses produce 502 with distinct log reasons.
/// A valid Declined decision is a business outcome, not a bank failure. Card details are never logged.
/// </remarks>
public class BankClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BankClient> _logger;

    public BankClient(HttpClient httpClient, ILogger<BankClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
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

        var startedAt = Stopwatch.GetTimestamp();
        int? statusCode = null;

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("payments", body, cancellationToken);
            statusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                LogFailure("UnexpectedStatus", startedAt, statusCode);
                return null;
            }

            var bankResponse = await response.Content.ReadFromJsonAsync<BankPaymentResponse>(cancellationToken);

            var authorized = bankResponse?.Authorized;

            if (!authorized.HasValue)
            {
                LogFailure("MissingDecision", startedAt, statusCode);
                return null;
            }

            var status = authorized.Value ? PaymentStatus.Authorized : PaymentStatus.Declined;

            _logger.LogInformation(
                "Bank answered {Status} in {ElapsedMs:0.0}ms (HTTP {StatusCode})",
                status,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                (int)response.StatusCode);

            return status;
        }
        catch (HttpRequestException exception)
        {
            // Exception messages can include external data; retain only the classified error.
            LogFailure("TransportError", startedAt, statusCode, exception.HttpRequestError);
            return null;
        }
        catch (JsonException)
        {
            LogFailure("UnreadableResponse", startedAt, statusCode);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure("Timeout", startedAt, statusCode);
            return null;
        }
    }

    private void LogFailure(
        string reason,
        long startedAt,
        int? statusCode = null,
        HttpRequestError? httpRequestError = null)
    {
        _logger.LogWarning(
            "Bank call failed: {FailureReason} after {ElapsedMs:0.0}ms (HTTP {StatusCode}, transport error {HttpRequestError})",
            reason,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            statusCode,
            httpRequestError);
    }

    // Model the bank's authorization code, although this exercise does not store or return it.
    private sealed record BankPaymentResponse(
        bool? Authorized,
        [property: JsonPropertyName("authorization_code")] string? AuthorizationCode);
}
