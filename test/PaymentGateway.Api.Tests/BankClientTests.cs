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

    [Fact]
    public async Task ReturnsAuthorizedWhenTheBankAuthorizes()
    {
        var handler = new StubBankHandler(_ => StubBankHandler.Json(
            HttpStatusCode.OK,
            """{"authorized": true, "authorization_code": "9f8c1b6e"}"""));

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
