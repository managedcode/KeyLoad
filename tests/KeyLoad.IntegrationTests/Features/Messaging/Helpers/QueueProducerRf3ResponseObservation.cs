using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Borrows the original caller lifetime; never invents a transport exception or result.</summary>
internal sealed class QueueProducerRf3ResponseObservation(HttpMessageHandler inner, Guid command,
    Func<Task> cancelCaller) : DelegatingHandler(inner)
{
    internal bool Observed { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var selected = request.RequestUri?.AbsolutePath == QueueProducerRf3Protocol.CommandPath
            && request.Headers.TryGetValues(QueueProducerRf3Protocol.CommandHeader, out var ids)
            && ids.SingleOrDefault() == command.ToString();
        var response = await base.SendAsync(request, token).ConfigureAwait(false);
        if (selected && response.IsSuccessStatusCode)
        {
            Observed = true;
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(cancelCaller, failures).ConfigureAwait(false);
            if (failures.Count != QueueProducerRf3Protocol.NoFailures)
            {
                ServerFailureObserver.Observe(response.Dispose, failures);
                ServerFailureObserver.ThrowIfAny(failures);
            }
        }
        return response;
    }
}
