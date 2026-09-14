using System.Net;
using System.Text.Json;

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
        var handler = new StubBankHandler(_ => StubBankHandler.Json(HttpStatusCode.OK, json));

        var status = await CreateClient(handler).AuthorizeAsync(Request, default);

        Assert.Null(status);
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
        var handler = new StubBankHandler(_ => StubBankHandler.Json(
            HttpStatusCode.OK,
            """{"authorized": false, "authorization_code": ""}"""));

        var status = await CreateClient(handler).AuthorizeAsync(Request, default);

        Assert.Equal(PaymentStatus.Declined, status);
    }

    [Fact]
    public async Task ReturnsNullWhenTheBankReturnsAnErrorStatus()
    {
        var handler = new StubBankHandler(_ => StubBankHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"));

        var status = await CreateClient(handler).AuthorizeAsync(Request, default);

        Assert.Null(status);
    }

    [Fact]
    public async Task ReturnsNullWhenTheBankIsUnreachable()
    {
        var handler = new StubBankHandler(_ => throw new HttpRequestException("connection refused"));

        var status = await CreateClient(handler).AuthorizeAsync(Request, default);

        Assert.Null(status);
    }

    [Fact]
    public async Task ReturnsNullWhenTheBankTimesOut()
    {
        // HttpClient reports a timeout as cancellation with an inner TimeoutException.
        var handler = new StubBankHandler(_ => throw new TaskCanceledException(
            "The bank request timed out.", new TimeoutException()));

        var status = await CreateClient(handler).AuthorizeAsync(Request, default);

        Assert.Null(status);
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(handler).AuthorizeAsync(Request, cancellation.Token));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ReturnsNullWhenTheBankResponseIsUnreadable()
    {
        var handler = new StubBankHandler(_ => StubBankHandler.Json(HttpStatusCode.OK, "not json"));

        var status = await CreateClient(handler).AuthorizeAsync(Request, default);

        Assert.Null(status);
    }

    private static BankClient CreateClient(HttpMessageHandler handler)
    {
        return new BankClient(new HttpClient(handler) { BaseAddress = new Uri("http://bank.test/") });
    }
}
