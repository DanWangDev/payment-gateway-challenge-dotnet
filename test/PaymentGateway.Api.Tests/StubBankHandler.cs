using System.Net;
using System.Text;

namespace PaymentGateway.Api.Tests;

/// <summary>
/// Test double for the bank: records every request and answers with whatever the test supplies, so call
/// count can be asserted as well as the response.
/// </summary>
internal sealed class StubBankHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubBankHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public List<CapturedRequest> Requests { get; } = [];

    public int CallCount => Requests.Count;

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri,
            request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));

        return _respond(request);
    }

    internal sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string Body);
}
