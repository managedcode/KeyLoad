using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class ControlledDocumentTargetEffect(ControlledDocumentPhaseExecution phases, TimeProvider clock)
{
    internal async Task<(PartitionControlEffectPayload Effect, Guid GrantId)> ExecuteAsync(
        ReplicatedOperation original, PartitionControlDocumentCommandContext context,
        PartitionControlCommandRecord admission, DateTimeOffset requestExpiry,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var delegation = admission.Delegation
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.Unavailable);
        var body = admission.TargetBody;
        if (body.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.Unavailable); }
        var grantId = ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.Authorize);
        var proposed = phases.ProposeEncoded(context, PartitionMovePeerStage.ControlApplyCommand, body);
        var originalAuthorization = new PartitionMoveAuthorizeBody(grantId, admission.EffectId,
            context.OperatorPrincipalId, proposed, context.Control.DestinationOwner, delegation.ExpiresAt);
        if (delegation.ExpiresAt > clock.GetUtcNow())
        {
            await phases.ApplyAsync(grantId, context, PartitionMovePeerStage.ControlAuthorize,
                originalAuthorization, requestExpiry,
                null, null, work, cancellationToken).ConfigureAwait(false);
        }
        var authorization = await phases.CaptureGrantAsync(context, originalAuthorization,
            requestExpiry, work, cancellationToken).ConfigureAwait(false);
        var grant = authorization.Grant;
        var result = grant.Settlement is not null || grant.AbortDisposition is not null
            || delegation.ExpiresAt <= clock.GetUtcNow()
            ? await phases.QueryOriginalEncodedAsync(admission.EffectId, context, body, grant,
                authorization.Authorization, requestExpiry, work, cancellationToken).ConfigureAwait(false)
            : await phases.ApplyEncodedAsync(admission.EffectId, context, PartitionMovePeerStage.ControlApplyCommand,
                body, delegation.ExpiresAt, grant, authorization.Authorization, work, cancellationToken).ConfigureAwait(false);
        var effect = result.ControlledEffect
            ?? throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteDocumentProtocol.Unavailable);
        await phases.ApplyAsync(ControlledDocumentTechnicalIdentity.Derive(original,
            ControlledDocumentTechnicalIdentity.GrantAcknowledgement), context,
            PartitionMovePeerStage.ControlAcknowledge, new PartitionMoveAcknowledgeBody(grantId, result.Journal),
            requestExpiry, null, null, work, cancellationToken).ConfigureAwait(false);
        await phases.ObserveGrantSettledAsync(work, cancellationToken).ConfigureAwait(false);
        return (effect, grantId);
    }
}
