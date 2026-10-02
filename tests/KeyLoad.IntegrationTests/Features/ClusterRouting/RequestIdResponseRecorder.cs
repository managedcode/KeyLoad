using System.Net;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class RequestIdResponseRecorder(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    private const int MaximumReceipts = 64;
    private readonly object gate = new();
    private readonly List<RequestIdReceipt> receipts = [];
    private bool OverflowedValue { get; set; }

    public bool Overflowed
    {
        get
        {
            lock (gate)
            {
                return OverflowedValue;
            }
        }
    }

    public RequestIdReceipt[] Snapshot()
    {
        lock (gate)
        {
            return receipts.ToArray();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var values = response.Headers.TryGetValues("X-KeyLoad-Request-Id", out var headerValues)
            ? headerValues.ToArray()
            : [];
        var receipt = new RequestIdReceipt(request.RequestUri?.AbsolutePath ?? "", response.StatusCode, values);
        lock (gate)
        {
            if (receipts.Count == MaximumReceipts)
            {
                OverflowedValue = true;
            }
            else
            {
                receipts.Add(receipt);
            }
        }
        return response;
    }
}

internal sealed record RequestIdReceipt(string Path, HttpStatusCode StatusCode, string[] HeaderValues);
