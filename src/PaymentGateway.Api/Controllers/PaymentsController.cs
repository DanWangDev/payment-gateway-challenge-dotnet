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
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        PaymentsRepository paymentsRepository,
        BankClient bankClient,
        ILogger<PaymentsController> logger)
    {
        _paymentsRepository = paymentsRepository;
        _bankClient = bankClient;
        _logger = logger;
    }

    /// <summary>Retrieve a stored payment.</summary>
    /// <param name="id">The payment ID returned by POST.</param>
    /// <response code="200">The stored Authorized or Declined payment, with only the card's last four digits.</response>
    /// <response code="404">No payment exists with this ID in this gateway instance.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GetPaymentResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public ActionResult<GetPaymentResponse> GetPayment(Guid id)
    {
        var payment = _paymentsRepository.Get(id);

        return payment is null ? NotFound() : Ok(ToResponse(payment));
    }

    /// <summary>Submit a card payment to the acquiring bank.</summary>
    /// <remarks>
    /// Both Authorized and Declined decisions create a payment. Follow the Location response header
    /// to retrieve it. Invalid requests never reach the bank. A bank failure provides no reliable
    /// decision; retrying may duplicate a charge because this API does not implement idempotency.
    /// </remarks>
    /// <response code="201">An Authorized or Declined payment was stored; Location identifies its GET URL.</response>
    /// <response code="400">Validation or request binding failed. Status is Rejected and nothing is stored.</response>
    /// <response code="502">The bank failed, timed out, or supplied no usable decision. Nothing is stored.</response>
    [HttpPost]
    [ProducesResponseType(typeof(PostPaymentResponse), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(RejectedPaymentResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
    public async Task<ActionResult<PostPaymentResponse>> PostPayment(
        PostPaymentRequest request,
        CancellationToken cancellationToken)
    {
        // Normalise first so the validated, stored and bank-submitted currency are the same value.
        request.Currency = request.Currency.Trim().ToUpperInvariant();

        var errors = PaymentRequestValidator.Validate(request, DateOnly.FromDateTime(DateTime.UtcNow));
        if (errors.Count > 0)
        {
            _logger.LogInformation("Payment rejected: {Errors}", string.Join("; ", errors));
            return BadRequest(new RejectedPaymentResponse { Errors = errors });
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
        _logger.LogInformation("Payment {PaymentId} {Status}", payment.Id, payment.Status);

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
