using KeyLoad.Core.TimeSeries;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

internal sealed class SampleChunkJobDispatch(BackgroundStorageClient storage, GrainRequestCodec codec,
    IServiceProvider services, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing)
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
        var payload = OrleansSerialization.Serialize(new CommandRequest(hint.CommandId, hint.Partition,
            [SampleChunkJobIdentity.Mutation(hint)]));
        var connectionId = ConnectionExecutionIdentity.Resolve(services);
        using var identity = new GrainRequestIdentityScope(services, principal, requestId, hint.CommandId, cancellationToken, connectionId: connectionId);
        var envelope = codec.CreateCommand(requestId, principal.Id, OperationKind.Batch, hint.CommandId, payload);
        var request = grains.GetGrain<IConnectionGrain>(connectionId);
        return await GrainRequestStreamConsumer.DrainAsync(
            createStream: token => request.ExecuteStreamAsync(envelope, token), serializer: serializer,
            requestId: requestId, clock: clock, cancellationToken: cancellationToken, options: routing).ConfigureAwait(true);
    }
}
