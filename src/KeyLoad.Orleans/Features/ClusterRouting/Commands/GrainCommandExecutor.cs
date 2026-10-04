using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal sealed class GrainCommandExecutor(DatabaseEngine database, ICommitCoordinator coordinator, TimeProvider clock)
{
    internal async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request, string actorKey, CancellationToken cancellationToken)
    {
        var stage = GrainFailureStage.PartitionResolution;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            GrainIdentityContext.Validate(request.Envelope, request.Envelope.RequestId);
            if (GrainPartitionResolver.Resolve(request) != actorKey)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            }

            stage = GrainFailureStage.QuorumRead;
            await coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            var envelope = request.Envelope;
            GrainRequestScope.Validate(envelope, database.Store.Identity.Incarnation, clock.GetUtcNow());
            GrainIdentityContext.Validate(envelope, envelope.RequestId);
            stage = GrainFailureStage.Authorization;
            var principal = GrainRequestAuthority.Reload(database, envelope.PrincipalId!, clock);
            var kind = envelope.CommandKind ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            stage = GrainFailureStage.CapabilityExecution;
            var result = await coordinator.SubmitNativeAsync(kind, envelope.CommandId, principal.Id,
                request.Payload, cancellationToken).ConfigureAwait(true);
            stage = GrainFailureStage.ReplyEncoding;
            return GrainReplyFactory.Operation(result, cancellationToken);
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            GrainFailureDiagnostics.Mark(error, stage);
            throw;
        }
    }
}
