namespace KeyLoad.Clustering;

internal sealed class DistributedSearchStatisticsObservation(GrainRequestCodec codec,
    DecodedGrainRequest request, Guid requestId, IGrainContext context)
{
    internal async Task ObserveAsync(CancellationToken token)
    {
        await codec.ObservePhaseAsync(request, GrainRequestPhase.DistributedSearchStatisticsCaptured,
            context, token);
        ConnectionReadExecution.ValidateFreshRequest(codec, request, requestId, token);
    }
}
