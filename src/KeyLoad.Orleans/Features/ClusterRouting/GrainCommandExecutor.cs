using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal sealed class GrainCommandExecutor(DatabaseEngine database, ICommitCoordinator coordinator, TimeProvider clock)
{
    internal async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request, string actorKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (GrainPartitionResolver.Resolve(request) != actorKey)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        await coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        var envelope = request.Envelope;
        var principal = GrainRequestAuthority.Reload(database, envelope.PrincipalId!, clock);
        var kind = envelope.CommandKind ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        var result = await coordinator.SubmitAsync(kind, envelope.CommandId, principal.Id,
            GrainPayloadJson.Utf8.GetString(request.Payload), cancellationToken).ConfigureAwait(true);
        return GrainReplyFactory.Operation(result, cancellationToken);
    }
}
