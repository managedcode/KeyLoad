using KeyLoad.Core.TimeSeries;

namespace KeyLoad.Clustering;

internal sealed class SampleChunkJobDispatch(BackgroundStorageClient storage, GrainRequestCodec codec,
    IServiceProvider services)
{
    internal async Task<GrainOperationReply> ExecuteAsync(IGrainFactory grains, SampleChunkWorkHint hint,
        CancellationToken cancellationToken)
    {
        var principal = await storage.Operations.ReadSampleChunkCreatorAsync(hint, cancellationToken).ConfigureAwait(true);
        return await DispatchAsync(grains, hint, principal, cancellationToken).ConfigureAwait(true);
    }

    private async Task<GrainOperationReply> DispatchAsync(IGrainFactory grains, SampleChunkWorkHint hint,
        PrincipalRecord principal, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var payload = new CommandRequest(hint.CommandId, hint.Partition,
            [SampleChunkJobIdentity.Mutation(hint)]);
        var connectionId = ConnectionExecutionIdentity.Resolve(services);
        using var identity = new GrainRequestIdentityScope(services, principal, requestId, hint.CommandId, cancellationToken, connectionId: connectionId);
        var envelope = codec.CreateCommand(requestId, principal.Id, OperationKind.Batch, hint.CommandId, payload);
        var request = grains.GetGrain<IConnectionGrain>(connectionId);
        return await GrainRequestStreamConsumer.DrainAsync(
            createStream: token => request.ExecuteStreamAsync(envelope, token),
            requestId: requestId, cancellationToken: cancellationToken).ConfigureAwait(true);
    }
}
