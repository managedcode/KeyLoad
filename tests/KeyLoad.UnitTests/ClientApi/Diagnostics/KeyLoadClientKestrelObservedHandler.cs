namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Passes original requests, tokens and unconsumed responses through the default native HTTP chain.</summary>
internal sealed class KeyLoadClientKestrelObservedHandler(KeyLoadClientKestrelObservation observation) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        observation.Record(KestrelObservationStage.SendStarted, token: cancellationToken);
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            observation.Record(KestrelObservationStage.ResponseReceived, (int)response.StatusCode, token: cancellationToken);
            return response;
        }
        catch (Exception error)
        {
            observation.Record(KestrelObservationStage.SendFailed, error: error, token: cancellationToken);
            throw;
        }
    }
}
