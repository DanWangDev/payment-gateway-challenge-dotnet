using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Resources;
using PaymentGateway.Api.Services;
using static PaymentGateway.Api.Resources.PaymentRejectionMessages;

namespace PaymentGateway.Api.Tests;

public class PaymentRequestValidatorTests
{
    private static readonly DateOnly ReferenceDate = new(2026, 9, 14);

    [Fact]
    public void AcceptsAValidRequest()
    {
        Assert.Empty(PaymentRequestValidator.Validate(ValidRequest(), ReferenceDate));
    }

    [Theory]
    [InlineData("1234567890123")]        // 13 - one short
    [InlineData("12345678901234567890")] // 20 - one long
    [InlineData("12345678901234a")]
    [InlineData("1234 5678 9012 34")]
    [InlineData("1234-5678-9012-34")]
    [InlineData("")]
    public void RejectsMalformedCardNumbers(string cardNumber)
    {
        var request = ValidRequest();
        request.CardNumber = cardNumber;

        Assert.Equal(CardNumberInvalid, Assert.Single(PaymentRequestValidator.Validate(request, ReferenceDate)));
    }

    [Theory]
    [InlineData("12345678901234")]      // 14 - lower bound
    [InlineData("1234567890123456789")] // 19 - upper bound
    public void AcceptsCardNumbersAtTheLengthBoundaries(string cardNumber)
    {
        var request = ValidRequest();
        request.CardNumber = cardNumber;

        Assert.Empty(PaymentRequestValidator.Validate(request, ReferenceDate));
    }

    [Fact]
    public void AcceptsACardExpiringThisMonth()
    {
        var request = ValidRequest();
        request.ExpiryMonth = ReferenceDate.Month;
        request.ExpiryYear = ReferenceDate.Year;

        Assert.Empty(PaymentRequestValidator.Validate(request, ReferenceDate));
    }

    [Fact]
    public void RejectsACardThatExpiredLastMonth()
    {
        var previousMonth = ReferenceDate.AddMonths(-1);
        var request = ValidRequest();
        request.ExpiryMonth = previousMonth.Month;
        request.ExpiryYear = previousMonth.Year;

        Assert.Equal(CardExpired, Assert.Single(PaymentRequestValidator.Validate(request, ReferenceDate)));
    }

    [Fact]
    public void RejectsACardFromLastYear()
    {
        var request = ValidRequest();
        request.ExpiryYear = ReferenceDate.Year - 1;

        Assert.Equal(CardExpired, Assert.Single(PaymentRequestValidator.Validate(request, ReferenceDate)));
    }

    [Fact]
    public void RejectsAMissingExpiryYear()
    {
        var request = ValidRequest();
        request.ExpiryYear = 0;

        Assert.Equal(CardExpired, Assert.Single(PaymentRequestValidator.Validate(request, ReferenceDate)));
    }

    [Fact]
    public void AcceptsACardExpiringInJanuaryWhenTodayIsInDecember()
    {
        var request = ValidRequest();
        request.ExpiryMonth = 1;
        request.ExpiryYear = 2027;

        Assert.Empty(PaymentRequestValidator.Validate(request, new DateOnly(2026, 12, 15)));
    }

    [Fact]
    public void RejectsLastDecemberWhenTodayIsInJanuary()
    {
        var request = ValidRequest();
        request.ExpiryMonth = 12;
        request.ExpiryYear = 2026;

        Assert.Equal(
            CardExpired,
            Assert.Single(PaymentRequestValidator.Validate(request, new DateOnly(2027, 1, 15))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void RejectsImpossibleExpiryMonths(int month)
    {
        var request = ValidRequest();
        request.ExpiryMonth = month;
        request.ExpiryYear = ReferenceDate.Year + 1;

        Assert.Equal(ExpiryMonthInvalid, Assert.Single(PaymentRequestValidator.Validate(request, ReferenceDate)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void AcceptsMonthBoundaries(int month)
    {
        var request = ValidRequest();
        request.ExpiryMonth = month;
        // Use next year so January is still valid.
        request.ExpiryYear = ReferenceDate.Year + 1;

        Assert.Empty(PaymentRequestValidator.Validate(request, ReferenceDate));
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("USD")]
    [InlineData("EUR")]
    public void AcceptsSupportedCurrencies(string currency)
    {
        var request = ValidRequest();
        request.Currency = currency;

        Assert.Empty(PaymentRequestValidator.Validate(request, ReferenceDate));
    }

    [Theory]
    [InlineData("")]
    [InlineData("GB")]
    [InlineData("GBPP")]
    [InlineData("XYZ")] // well-formed ISO 4217 shape, but unsupported
    [InlineData("gbp")] // The validator expects uppercase currency codes.
    public void RejectsUnsupportedCurrencies(string currency)
    {
        var request = ValidRequest();
        request.Currency = currency;

        Assert.Equal(CurrencyUnsupported, Assert.Single(PaymentRequestValidator.Validate(request, ReferenceDate)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveAmounts(int amount)
    {
        var request = ValidRequest();
        request.Amount = amount;

        Assert.Equal(AmountInvalid, Assert.Single(PaymentRequestValidator.Validate(request, ReferenceDate)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void AcceptsPositiveAmounts(int amount)
    {
        var request = ValidRequest();
        request.Amount = amount;

        Assert.Empty(PaymentRequestValidator.Validate(request, ReferenceDate));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("abc")]
    [InlineData("1a2")]
    public void RejectsMalformedCvvs(string cvv)
    {
        var request = ValidRequest();
        request.Cvv = cvv;

        Assert.Equal(CvvInvalid, Assert.Single(PaymentRequestValidator.Validate(request, ReferenceDate)));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234")]
    [InlineData("012")]  // Leading zero.
    [InlineData("0123")]
    public void AcceptsCvvsAtTheLengthBoundaries(string cvv)
    {
        var request = ValidRequest();
        request.Cvv = cvv;

        Assert.Empty(PaymentRequestValidator.Validate(request, ReferenceDate));
    }

    [Fact]
    public void RejectsNonAsciiDigits()
    {
        // U+0663 is an Arabic-Indic digit that char.IsDigit would accept.
        var request = ValidRequest();
        request.CardNumber = new string((char)0x0663, 16);
        request.Cvv = new string((char)0x0663, 3);

        Assert.Equal(
            new[] { CardNumberInvalid, CvvInvalid },
            PaymentRequestValidator.Validate(request, ReferenceDate));
    }

    [Fact]
    public void RejectsValuesEndingInANewline()
    {
        // Both are within the allowed lengths, so only the digit check can reject them.
        var request = ValidRequest();
        request.CardNumber = "1234567890123456\n";
        request.Cvv = "123\n";

        Assert.Equal(
            new[] { CardNumberInvalid, CvvInvalid },
            PaymentRequestValidator.Validate(request, ReferenceDate));
    }

    [Fact]
    public void RejectsWhitespaceOnlyCardCvvAndCurrency()
    {
        var request = ValidRequest();
        request.CardNumber = "   ";
        request.Cvv = " ";
        request.Currency = "   ";

        Assert.Equal(
            new[] { CardNumberInvalid, CurrencyUnsupported, CvvInvalid },
            PaymentRequestValidator.Validate(request, ReferenceDate));
    }

    [Fact]
    public void RejectsAnAllDefaultsRequest()
    {
        Assert.Equal(
            new[] { CardNumberInvalid, ExpiryMonthInvalid, CurrencyUnsupported, AmountInvalid, CvvInvalid },
            PaymentRequestValidator.Validate(new PostPaymentRequest(), ReferenceDate));
    }

    [Fact]
    public void ReportsEveryFailedRule()
    {
        var request = new PostPaymentRequest
        {
            CardNumber = "not-a-card",
            ExpiryMonth = 13,
            ExpiryYear = 1900,
            Currency = "XXX",
            Amount = 0,
            Cvv = "1"
        };

        Assert.Equal(
            new[] { CardNumberInvalid, ExpiryMonthInvalid, CurrencyUnsupported, AmountInvalid, CvvInvalid },
            PaymentRequestValidator.Validate(request, ReferenceDate));
    }

    [Fact]
    public void RejectsNullFieldsInsteadOfThrowing()
    {
        var request = ValidRequest();
        request.CardNumber = null!;
        request.Currency = null!;
        request.Cvv = null!;

        Assert.Equal(
            new[] { CardNumberInvalid, CurrencyUnsupported, CvvInvalid },
            PaymentRequestValidator.Validate(request, ReferenceDate));
    }

    private static PostPaymentRequest ValidRequest()
    {
        var expiry = ReferenceDate.AddMonths(1);

        return new PostPaymentRequest
        {
            CardNumber = "1234567890123456",
            ExpiryMonth = expiry.Month,
            ExpiryYear = expiry.Year,
            Currency = "GBP",
            Amount = 100,
            Cvv = "123"
        };
    }
}
