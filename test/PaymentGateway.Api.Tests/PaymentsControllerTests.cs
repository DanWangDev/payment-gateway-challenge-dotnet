using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class PaymentsControllerTests
{
    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web)
    {
        // Rejecting integer enum values is what makes the status assertions below prove that the wire
        // carries the name ("Declined"), not the ordinal (1).
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    [Fact]
    public async Task RetrievesAStoredPayment()
    {
        var stored = StoredPayment(PaymentStatus.Declined);
        var factory = CreateFactory(BankMustNotBeCalled());
        factory.Repository.Add(stored);

        var response = await factory.CreateClient().GetAsync($"/api/Payments/{stored.Id}");
        var payment = (await response.Content.ReadFromJsonAsync<GetPaymentResponse>(ResponseJson))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("0007", payment.CardNumberLastFour);
        Assert.Equal(PaymentStatus.Declined, payment.Status);
    }

    [Fact]
    public async Task Returns404WhenThePaymentIsUnknown()
    {
        var factory = CreateFactory(BankMustNotBeCalled());

        var response = await factory.CreateClient().GetAsync($"/api/Payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreatesAnAuthorizedPaymentWhenTheBankAuthorizes()
    {
        var bank = BankReturns(HttpStatusCode.OK,
            """{"authorized": true, "authorization_code": "0bb07405-6d44-4b50-a14f-7ae0beff13ad"}""");
        var factory = CreateFactory(bank);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/Payments", ValidRequest());
        var payment = (await response.Content.ReadFromJsonAsync<PostPaymentResponse>(ResponseJson))!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.EndsWith($"/api/Payments/{payment.Id}", response.Headers.Location!.ToString());
        Assert.Equal(1, bank.CallCount);
    }

    [Fact]
    public async Task CreatesADeclinedPaymentWhenTheBankDeclines()
    {
        var bank = BankReturns(HttpStatusCode.OK, """{"authorized": false, "authorization_code": ""}""");
        var factory = CreateFactory(bank);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/Payments", ValidRequest());
        var payment = (await response.Content.ReadFromJsonAsync<PostPaymentResponse>(ResponseJson))!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(PaymentStatus.Declined, payment.Status);
    }

    [Fact]
    public async Task StoresWhatItReturns()
    {
        var factory = CreateFactory(BankReturns(HttpStatusCode.OK, """{"authorized": true}"""));
        var client = factory.CreateClient();

        var postResponse = await client.PostAsJsonAsync("/api/Payments", ValidRequest());
        var posted = (await postResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(ResponseJson))!;

        // Follow the Location the API reported rather than rebuilding the URL here.
        var getResponse = await client.GetAsync(postResponse.Headers.Location!);
        var retrieved = (await getResponse.Content.ReadFromJsonAsync<GetPaymentResponse>(ResponseJson))!;

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(posted.Id, retrieved.Id);
        Assert.Equal(posted.Status, retrieved.Status);
        Assert.Equal(posted.CardNumberLastFour, retrieved.CardNumberLastFour);
        Assert.Equal(posted.ExpiryMonth, retrieved.ExpiryMonth);
        Assert.Equal(posted.ExpiryYear, retrieved.ExpiryYear);
        Assert.Equal(posted.Currency, retrieved.Currency);
        Assert.Equal(posted.Amount, retrieved.Amount);
    }

    [Fact]
    public async Task StoresDeclinedPayments()
    {
        // Reconciliation has to be able to retrieve declined attempts, not just authorized ones.
        var factory = CreateFactory(BankReturns(HttpStatusCode.OK, """{"authorized": false}"""));
        var client = factory.CreateClient();

        var postResponse = await client.PostAsJsonAsync("/api/Payments", ValidRequest());
        var posted = (await postResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(ResponseJson))!;

        var getResponse = await client.GetAsync($"/api/Payments/{posted.Id}");
        var retrieved = (await getResponse.Content.ReadFromJsonAsync<GetPaymentResponse>(ResponseJson))!;

        Assert.Equal(PaymentStatus.Declined, retrieved.Status);
    }

    [Fact]
    public async Task NormalisesTheCurrencyBeforeStoringIt()
    {
        var bank = BankReturns(HttpStatusCode.OK, """{"authorized": true}""");
        var factory = CreateFactory(bank);
        var request = ValidRequest();
        request.Currency = " gbp ";

        var response = await factory.CreateClient().PostAsJsonAsync("/api/Payments", request);
        var payment = (await response.Content.ReadFromJsonAsync<PostPaymentResponse>(ResponseJson))!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("GBP", payment.Currency);
        Assert.Equal("GBP", factory.Repository.Get(payment.Id)!.Currency);
        Assert.Contains("\"currency\":\"GBP\"", Assert.Single(bank.Requests).Body);
    }

    [Fact]
    public async Task Returns502AndStoresNothingWhenTheBankIsUnavailable()
    {
        var bank = BankReturns(HttpStatusCode.ServiceUnavailable, "{}");
        var factory = CreateFactory(bank);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/Payments", ValidRequest());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1, bank.CallCount);
        Assert.Empty(factory.Repository.Payments);
    }

    [Fact]
    public async Task Returns502AndStoresNothingWhenTheBankCannotBeReached()
    {
        var bank = new StubBankHandler(_ => throw new HttpRequestException("connection refused"));
        var factory = CreateFactory(bank);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/Payments", ValidRequest());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1, bank.CallCount);
        Assert.Empty(factory.Repository.Payments);
    }

    [Fact]
    public async Task RejectsAnInvalidRequestWithoutCallingTheBank()
    {
        var bank = BankMustNotBeCalled();
        var factory = CreateFactory(bank);
        var request = ValidRequest();
        request.CardNumber = "1234";

        var response = await factory.CreateClient().PostAsJsonAsync("/api/Payments", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, bank.CallCount);
        Assert.Empty(factory.Repository.Payments);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Rejected", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "Card number must be 14 to 19 digits.",
            body.RootElement.GetProperty("errors")[0].GetString());
    }

    [Theory]
    // Binding fails because expiryMonth is not a number.
    [InlineData("""{"cardNumber": "1234567890123456", "expiryMonth": "not a number"}""")]
    // Binding fails because a non-nullable member was explicitly null - which is also what lets the
    // action normalise Currency without a null guard.
    [InlineData("""{"cardNumber": null, "expiryMonth": 12, "expiryYear": 2099, "currency": "GBP", "amount": 1050, "cvv": "123"}""")]
    public async Task ReportsUnbindableRequestsWithTheRejectedContract(string json)
    {
        var bank = BankMustNotBeCalled();
        var factory = CreateFactory(bank);
        var body = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await factory.CreateClient().PostAsync("/api/Payments", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, bank.CallCount);
        Assert.Empty(factory.Repository.Payments);

        using var responseBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Rejected", responseBody.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Array, responseBody.RootElement.GetProperty("errors").ValueKind);
        Assert.Equal(
            "The request body could not be read.",
            responseBody.RootElement.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task ReturnsNoSensitiveCardData()
    {
        const string cardNumber = "1234567890120007";
        var factory = CreateFactory(BankReturns(HttpStatusCode.OK, """{"authorized": true}"""));
        var client = factory.CreateClient();
        var request = ValidRequest();
        request.CardNumber = cardNumber;

        var postResponse = await client.PostAsJsonAsync("/api/Payments", request);
        var posted = (await postResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(ResponseJson))!;

        var getResponse = await client.GetAsync(postResponse.Headers.Location!);
        var retrieved = (await getResponse.Content.ReadFromJsonAsync<GetPaymentResponse>(ResponseJson))!;

        // Exactly the last four, leading zero included - not the full number, and not a parsed int.
        Assert.Equal("0007", posted.CardNumberLastFour);
        Assert.Equal("0007", retrieved.CardNumberLastFour);

        foreach (var body in new[]
        {
            await postResponse.Content.ReadAsStringAsync(),
            await getResponse.Content.ReadAsStringAsync()
        })
        {
            Assert.DoesNotContain(cardNumber, body);
            Assert.DoesNotContain("cvv", body, StringComparison.OrdinalIgnoreCase);
            // Quoted, so that cardNumberLastFour does not match.
            Assert.DoesNotContain("\"cardNumber\"", body);
            Assert.Contains("\"cardNumberLastFour\"", body);
        }
    }

    private static PostPaymentRequest ValidRequest()
    {
        return new PostPaymentRequest
        {
            CardNumber = "1234567890123456",
            ExpiryMonth = 12,
            // Far enough ahead that the test does not expire.
            ExpiryYear = 2099,
            Currency = "GBP",
            Amount = 1050,
            Cvv = "123"
        };
    }

    private static PostPaymentResponse StoredPayment(PaymentStatus status)
    {
        return new PostPaymentResponse
        {
            Id = Guid.NewGuid(),
            Status = status,
            CardNumberLastFour = "0007",
            ExpiryMonth = 12,
            ExpiryYear = 2099,
            Currency = "GBP",
            Amount = 1050
        };
    }

    private static PaymentsApiFactory CreateFactory(StubBankHandler bank)
    {
        return new PaymentsApiFactory(bank);
    }

    private static StubBankHandler BankReturns(HttpStatusCode statusCode, string body)
    {
        return new StubBankHandler(_ => StubBankHandler.Json(statusCode, body));
    }

    private static StubBankHandler BankMustNotBeCalled()
    {
        return new StubBankHandler(_ => throw new InvalidOperationException("The bank should not have been called."));
    }
}

internal sealed class PaymentsApiFactory : WebApplicationFactory<Program>
{
    private readonly HttpMessageHandler _bankHandler;

    public PaymentsApiFactory(HttpMessageHandler bankHandler)
    {
        _bankHandler = bankHandler;
    }

    public PaymentsRepository Repository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(Repository);

            // A typed client is registered under the simple type name - not the full name. If this ever
            // stops matching, the client silently makes real calls and the 502 tests fail on call count.
            services.Configure<HttpClientFactoryOptions>(nameof(BankClient), options =>
                options.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = _bankHandler));
        });
    }
}