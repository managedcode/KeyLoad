namespace KeyLoad.Orleans;

internal sealed class DistributedSearchStatisticsObservation(GrainRequestCodec codec,
    DecodedGrainRequest request, Guid requestId, IGrainContext context)
{
    internal async Task ObserveAsync(CancellationToken token)
    {
        await codec.ObservePhaseAsync(request, GrainRequestPhase.DistributedSearchStatisticsCaptured,
            context, token).ConfigureAwait(true);
        ConnectionReadExecution.ValidateFreshRequest(codec, request, requestId, token);
    }
}
