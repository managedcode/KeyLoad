using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Executes actual A admission, granted B effect and both durable A acknowledgements.</summary>
internal static class ControlledDocumentNativeCommandStages
{
    internal static async Task<PartitionControlEffectPayload> ExecuteAsync(ControlledDocumentNativePhaseFlow phases,
        ReplicatedOperation original, PartitionControlDocumentCommandContext context,
        DateTimeOffset expiry, CancellationToken token)
    {
        var effectId = ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.Effect);
        var admitted = await phases.ApplyAsync(ControlledDocumentTechnicalIdentity.Derive(original,
            ControlledDocumentTechnicalIdentity.Admit), context, PartitionMovePeerStage.ControlAdmitCommand,
            new PartitionControlAdmitBody(context.OperatorPrincipalId, context.Control, original, effectId, expiry),
            expiry, null, null, token);
        var record = admitted.ControlledCommand
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var observed = await ApplyTargetAsync(phases, original, context, record, expiry, token);
        await phases.ApplyAsync(ControlledDocumentTechnicalIdentity.Derive(original,
            ControlledDocumentTechnicalIdentity.CommandAcknowledgement), context,
            PartitionMovePeerStage.ControlAcknowledgeCommand,
            new PartitionControlAcknowledgeBody(context.OperatorPrincipalId, record.Identity,
                effectId, observed.Effect.Receipt, observed.Effect.OriginalResult, observed.GrantId),
            expiry, null, null, token);
        var finalized = await phases.ApplyAsync(ControlledDocumentTechnicalIdentity.Derive(original,
            ControlledDocumentTechnicalIdentity.Finalize), context, PartitionMovePeerStage.ControlFinalizeCommand,
            new PartitionControlFinalizeBody(context.OperatorPrincipalId, record.Identity, effectId),
            expiry, null, null, token);
        var finalRecord = finalized.ControlledCommand
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        await Assert.That(finalRecord.Phase).IsEqualTo(PartitionControlCommandPhase.Finalized);
        return observed.Effect;
    }

    private static async Task<(PartitionControlEffectPayload Effect, Guid GrantId)> ApplyTargetAsync(
        ControlledDocumentNativePhaseFlow phases, ReplicatedOperation original,
        PartitionControlDocumentCommandContext context, PartitionControlCommandRecord record,
        DateTimeOffset expiry, CancellationToken token)
    {
        var delegation = record.Delegation
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var body = record.TargetBody;
        if (body.IsEmpty)
        { throw new InvalidOperationException(PartitionMoveProtocol.Invalid); }
        var grantId = ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.Authorize);
        var authorized = await phases.ApplyAsync(grantId, context, PartitionMovePeerStage.ControlAuthorize,
            new PartitionMoveAuthorizeBody(grantId, record.EffectId, context.OperatorPrincipalId,
                phases.ProposeEncoded(context, PartitionMovePeerStage.ControlApplyCommand, body),
                context.Control.DestinationOwner, delegation.ExpiresAt), expiry, null, null, token);
        var grant = authorized.Grant ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var actual = await phases.ApplyEncodedAsync(record.EffectId, context, PartitionMovePeerStage.ControlApplyCommand,
            body, delegation.ExpiresAt, grant, authorized.Journal, token);
        var effect = actual.ControlledEffect
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        await phases.ApplyAsync(ControlledDocumentTechnicalIdentity.Derive(original,
            ControlledDocumentTechnicalIdentity.GrantAcknowledgement), context,
            PartitionMovePeerStage.ControlAcknowledge, new PartitionMoveAcknowledgeBody(grantId, actual.Journal),
            expiry, null, null, token);
        return (effect, grantId);
    }
}
