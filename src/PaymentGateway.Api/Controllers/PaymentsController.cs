using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : Controller
{
    private readonly PaymentsRepository _paymentsRepository;
    private readonly BankClient _bankClient;

    public PaymentsController(PaymentsRepository paymentsRepository, BankClient bankClient)
    {
        _paymentsRepository = paymentsRepository;
        _bankClient = bankClient;
    }

    [HttpGet("{id:guid}")]
    public ActionResult<GetPaymentResponse> GetPayment(Guid id)
    {
        var payment = _paymentsRepository.Get(id);

        return payment is null ? NotFound() : Ok(ToResponse(payment));
    }

    [HttpPost]
    public async Task<ActionResult<PostPaymentResponse>> PostPayment(
        PostPaymentRequest request,
        CancellationToken cancellationToken)
    {
        // Normalise first so the validated, stored and bank-submitted currency are the same value.
        request.Currency = request.Currency.Trim().ToUpperInvariant();

        var errors = PaymentRequestValidator.Validate(request, DateOnly.FromDateTime(DateTime.UtcNow));
        if (errors.Count > 0)
        {
            return BadRequest(new { status = PaymentStatus.Rejected, errors });
        }

        var status = await _bankClient.AuthorizeAsync(request, cancellationToken);
        if (status is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        var payment = new PostPaymentResponse
        {
            Id = Guid.NewGuid(),
            Status = status.Value,
            CardNumberLastFour = request.CardNumber[^4..],
            ExpiryMonth = request.ExpiryMonth,
            ExpiryYear = request.ExpiryYear,
            Currency = request.Currency,
            Amount = request.Amount
        };

        _paymentsRepository.Add(payment);

        // Action names are matched without the Async suffix: MVC strips it for URL generation by default.
        return CreatedAtAction(nameof(GetPayment), new { id = payment.Id }, payment);
    }

    private static GetPaymentResponse ToResponse(PostPaymentResponse payment)
    {
        return new GetPaymentResponse
        {
            Id = payment.Id,
            Status = payment.Status,
            CardNumberLastFour = payment.CardNumberLastFour,
            ExpiryMonth = payment.ExpiryMonth,
            ExpiryYear = payment.ExpiryYear,
            Currency = payment.Currency,
            Amount = payment.Amount
        };
    }
}