using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed class NativeAnnWaitHttpHandler(HttpMessageHandler inner,
    NativeAnnWaitHttpObservation events) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        events.Started(cancellationToken);
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            try
            {
                events.Failed(error, cancellationToken);
            }
            catch (Exception observation)
            { throw new AggregateException(error, observation); }
            throw;
        }
        try
        {
            events.Received((int)response.StatusCode, cancellationToken);
            return response;
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            ServerFailureObserver.Observe(response.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
}
