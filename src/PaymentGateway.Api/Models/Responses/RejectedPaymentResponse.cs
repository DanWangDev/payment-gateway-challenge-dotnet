namespace PaymentGateway.Api.Models.Responses;

/// <summary>A request rejected before contacting the bank. No payment is stored.</summary>
public class RejectedPaymentResponse
{
    /// <summary>Always Rejected.</summary>
    /// <example>Rejected</example>
    public PaymentStatus Status => PaymentStatus.Rejected;

    /// <summary>Validation errors, or a generic message when the request body cannot be bound.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
}
