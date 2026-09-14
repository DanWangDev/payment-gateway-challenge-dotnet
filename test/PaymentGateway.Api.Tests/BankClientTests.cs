using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class BankClientTests
{
    private static readonly PostPaymentRequest Request = new()
    {
        CardNumber = "1234567890123456",
        ExpiryMonth = 3,
        ExpiryYear = 2027,
        Currency = "GBP",
        Amount = 1050,
        Cvv = "123"
    };

    [Fact]
    public async Task SendsTheBankTheExpectedRequest()
    {
        var handler = new StubBankHandler(_ => StubBankHandler.Json(HttpStatusCode.OK, """{"authorized": true}"""));

        await CreateClient(handler).AuthorizeAsync(Request, default);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("http://bank.test/payments", sent.Uri?.ToString());

        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal("1234567890123456", body.RootElement.GetProperty("card_number").GetString());
        Assert.Equal("03/2027", body.RootElement.GetProperty("expiry_date").GetString());
        Assert.Equal("GBP", body.RootElement.GetProperty("currency").GetString());
        Assert.Equal(1050, body.RootElement.GetProperty("amount").GetInt32());
        Assert.Equal("123", body.RootElement.GetProperty("cvv").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"authorization_code": "3f2a1c9e-8b7d-4a6f-9c1e-2d4b6a8c0e5f"}""")]
    [InlineData("""{"authorized": null}""")]
    [InlineData("null")]
    public async Task ReturnsNullWhenTheBankProvidesNoDecision(string json)
    {
        var (client, logger) = CreateClientWithLogger(_ => StubBankHandler.Json(HttpStatusCode.OK, json));

        var status = await client.AuthorizeAsync(Request, default);

        Assert.Null(status);
        Assert.Equal("MissingDecision", Assert.Single(logger.Entries).Properties["FailureReason"]);
    }

    [Fact]
    public async Task ReturnsAuthorizedWhenTheBankAuthorizes()
    {
        var handler = new StubBankHandler(_ => StubBankHandler.Json(
            HttpStatusCode.OK,
            """{"authorized": true, "authorization_code": "0bb07405-6d44-4b50-a14f-7ae0beff13ad"}"""));

        var status = await CreateClient(handler).AuthorizeAsync(Request, default);

        Assert.Equal(PaymentStatus.Authorized, status);
    }

    [Fact]
    public async Task ReturnsDeclinedWhenTheBankDeclines()
    {
        var (client, logger) = CreateClientWithLogger(_ => StubBankHandler.Json(
            HttpStatusCode.OK,
            """{"authorized": false, "authorization_code": ""}"""));

        var status = await client.AuthorizeAsync(Request, default);

        Assert.Equal(PaymentStatus.Declined, status);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(PaymentStatus.Declined, entry.Properties["Status"]);
        Assert.Equal(200, entry.Properties["StatusCode"]);
        Assert.True(Assert.IsType<double>(entry.Properties["ElapsedMs"]) >= 0);
    }

    [Fact]
    public async Task ReturnsNullWhenTheBankReturnsAnErrorStatus()
    {
        var (client, logger) = CreateClientWithLogger(_ => StubBankHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"));

        var status = await client.AuthorizeAsync(Request, default);

        Assert.Null(status);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("UnexpectedStatus", entry.Properties["FailureReason"]);
        Assert.Equal(503, entry.Properties["StatusCode"]);
        Assert.True(Assert.IsType<double>(entry.Properties["ElapsedMs"]) >= 0);
    }

    [Fact]
    public async Task ReturnsNullWhenTheBankIsUnreachable()
    {
        var (client, logger) = CreateClientWithLogger(_ => throw new HttpRequestException(
            HttpRequestError.ConnectionError, "connection refused"));

        var status = await client.AuthorizeAsync(Request, default);

        Assert.Null(status);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("TransportError", entry.Properties["FailureReason"]);
        Assert.Equal(HttpRequestError.ConnectionError, entry.Properties["HttpRequestError"]);
        Assert.Null(entry.Properties["StatusCode"]);
    }

    [Fact]
    public async Task ReturnsNullWhenTheBankTimesOut()
    {
        // HttpClient reports a timeout as cancellation with an inner TimeoutException.
        var (client, logger) = CreateClientWithLogger(_ => throw new TaskCanceledException(
            "The bank request timed out.", new TimeoutException()));

        var status = await client.AuthorizeAsync(Request, default);

        Assert.Null(status);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("Timeout", entry.Properties["FailureReason"]);
    }

    [Fact]
    public async Task PropagatesCallerCancellationDuringTheBankRequest()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubBankHandler((_, cancellationToken) =>
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(StubBankHandler.Json(HttpStatusCode.OK, """{"authorized": true}"""));
        });

        var logger = new CapturingLogger<BankClient>();
        var client = CreateClient(handler, logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.AuthorizeAsync(Request, cancellation.Token));

        Assert.Equal(1, handler.CallCount);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task DoesNotLogCardDetailsFromAnException()
    {
        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 3,
            ExpiryYear = 2027,
            Currency = "GBP",
            Amount = 1050,
            Cvv = "9876"
        };
        var (client, logger) = CreateClientWithLogger(_ => throw new HttpRequestException(
            $"Failed for card {request.CardNumber}, CVV {request.Cvv}"));

        await client.AuthorizeAsync(request, default);

        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        foreach (var value in entry.Properties.Values.Append(entry.Message))
        {
            Assert.DoesNotContain(request.CardNumber, value?.ToString() ?? string.Empty);
            Assert.DoesNotContain(request.Cvv, value?.ToString() ?? string.Empty);
        }
    }

    [Fact]
    public async Task ReturnsNullWhenTheBankResponseIsUnreadable()
    {
        var (client, logger) = CreateClientWithLogger(_ => StubBankHandler.Json(HttpStatusCode.OK, "not json"));

        var status = await client.AuthorizeAsync(Request, default);

        Assert.Null(status);
        Assert.Equal("UnreadableResponse", Assert.Single(logger.Entries).Properties["FailureReason"]);
    }

    private static BankClient CreateClient(
        HttpMessageHandler handler,
        ILogger<BankClient>? logger = null)
    {
        return new BankClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://bank.test/") },
            logger ?? NullLogger<BankClient>.Instance);
    }

    private static (BankClient Client, CapturingLogger<BankClient> Logger) CreateClientWithLogger(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var logger = new CapturingLogger<BankClient>();
        var handler = new StubBankHandler(respond);

        return (CreateClient(handler, logger), logger);
    }
}
