using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal sealed class SampleChunkJobDispatch(DatabaseEngine database, GrainRequestCodec codec,
    IServiceProvider services, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing)
{
    internal Task<GrainOperationReply> ExecuteAsync(IGrainFactory grains, SampleChunkWorkHint hint,
        CancellationToken cancellationToken)
    {
        var principal = database.Store.Read(view => database.Principal(view, hint.Creator, clock.GetUtcNow()));
        if (principal.PolicyEpoch != hint.CreatorPolicyEpoch)
        { throw Errors.Fail(ErrorCode.RevisionConflict, SampleChunkJobProtocol.Invalid); }
        return DispatchAsync(grains, hint, principal, cancellationToken);
    }

    private async Task<GrainOperationReply> DispatchAsync(IGrainFactory grains, SampleChunkWorkHint hint,
        PrincipalRecord principal, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var payload = NativeSerialization.Serialize(new CommandRequest(hint.CommandId, hint.Partition,
            [SampleChunkJobIdentity.Mutation(hint)]));
        var connectionId = NativeConnectionExecutionIdentity.Resolve(services);
        using var identity = new GrainRequestIdentityScope(services, principal, requestId, hint.CommandId, cancellationToken, connectionId: connectionId);
        var signed = codec.CreateCommand(requestId, principal.Id, OperationKind.Batch, hint.CommandId, payload);
        var request = grains.GetGrain<IConnectionGrain>(connectionId);
        return await GrainRequestStreamConsumer.DrainAsync(
            createStream: token => request.ExecuteStreamAsync(signed, token), serializer: serializer,
            requestId: requestId, clock: clock, cancellationToken: cancellationToken, options: routing).ConfigureAwait(true);
    }
}
