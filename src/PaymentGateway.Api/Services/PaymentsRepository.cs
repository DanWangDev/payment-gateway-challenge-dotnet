using System.Collections.Concurrent;

using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class PaymentsRepository
{
    private readonly ConcurrentDictionary<Guid, PostPaymentResponse> _payments = new();

    public IReadOnlyDictionary<Guid, PostPaymentResponse> Payments => _payments;

    public void Add(PostPaymentResponse payment)
    {
        _payments.TryAdd(payment.Id, payment);
    }

    public PostPaymentResponse? Get(Guid id)
    {
        return _payments.TryGetValue(id, out var payment) ? payment : null;
    }
}
