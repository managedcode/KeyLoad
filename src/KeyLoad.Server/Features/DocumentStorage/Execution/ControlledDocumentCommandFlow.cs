using Microsoft.Extensions.Options;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class ControlledDocumentCommandFlow(PartitionHost partition,
    ControlledDocumentPhaseExecution phases, ControlledBlobSourceRead blobReads, Guid ingressRequestId,
    IOptions<DatabaseLimits> limits, TimeProvider clock)
{
    internal async Task<OperationResult> ExecuteAsync(ReplicatedOperation original,
        PartitionControlDocumentCommandContext context, DateTimeOffset requestExpiry,
        ReadExecutionBudget originalWork, CancellationToken cancellationToken)
    {
        if (context.OriginalOutcome is { } retained)
        { return BlobStorageOperations.Handles(original.Kind)
            ? await ObserveBlobAsync(original, requestExpiry, originalWork, cancellationToken).ConfigureAwait(false)
            : retained; }
        var admission = context.Admission;
        if (admission is null)
        {
            var phase = await phases.ApplyAsync(ControlledDocumentTechnicalIdentity.Derive(original,
                ControlledDocumentTechnicalIdentity.Admit), context, PartitionMovePeerStage.ControlAdmitCommand,
                new PartitionControlAdmitBody(context.OperatorPrincipalId, context.Control, original,
                    ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.Effect),
                    requestExpiry), requestExpiry, null, null, originalWork, cancellationToken).ConfigureAwait(false);
            admission = phase.ControlledCommand
                ?? throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteDocumentProtocol.Unavailable);
        }
        if (admission.Phase == PartitionControlCommandPhase.Admitted)
        {
            admission = await ApplyEffectAsync(original, context, admission, requestExpiry,
                originalWork, cancellationToken).ConfigureAwait(false);
        }
        if (admission.Phase == PartitionControlCommandPhase.EffectAcknowledged)
        {
            var phase = await phases.ApplyAsync(ControlledDocumentTechnicalIdentity.Derive(original,
                ControlledDocumentTechnicalIdentity.Finalize), context, PartitionMovePeerStage.ControlFinalizeCommand,
                new PartitionControlFinalizeBody(context.OperatorPrincipalId, admission.Identity, admission.EffectId),
                requestExpiry, null, null, originalWork, cancellationToken).ConfigureAwait(false);
            admission = phase.ControlledCommand
                ?? throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteDocumentProtocol.Unavailable);
        }
        if (admission.Phase != PartitionControlCommandPhase.Finalized)
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteDocumentProtocol.Unavailable); }
        await partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        if (BlobStorageOperations.Handles(original.Kind))
        { return await ObserveBlobAsync(original, requestExpiry, originalWork, cancellationToken).ConfigureAwait(false); }
        return partition.Database.ResolveControlledDocumentOutcome(original.PrincipalId, original, originalWork);
    }

    private async Task<OperationResult> ObserveBlobAsync(ReplicatedOperation original,
        DateTimeOffset expiry, ReadExecutionBudget work, CancellationToken token)
    {
        var queryExpiry = clock.GetUtcNow() + TimeSpan.FromSeconds(limits.Value.QueryDeadlineSeconds);
        if (queryExpiry > expiry)
        { queryExpiry = expiry; }
        var frame = partition.Database.TryCaptureControlledBlobOutcome(original.PrincipalId, original, queryExpiry, work)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteDocumentProtocol.Unavailable);
        return await blobReads.ObserveOutcomeAsync(ingressRequestId, frame, work, token).ConfigureAwait(false);
    }

    private async Task<PartitionControlCommandRecord> ApplyEffectAsync(ReplicatedOperation original,
        PartitionControlDocumentCommandContext context, PartitionControlCommandRecord admission,
        DateTimeOffset requestExpiry, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var effect = new ControlledDocumentTargetEffect(phases, clock);
        var observed = await effect.ExecuteAsync(original, context, admission, requestExpiry,
            work, cancellationToken).ConfigureAwait(false);
        var ack = await phases.ApplyAsync(ControlledDocumentTechnicalIdentity.Derive(original,
            ControlledDocumentTechnicalIdentity.CommandAcknowledgement), context,
            PartitionMovePeerStage.ControlAcknowledgeCommand,
            new PartitionControlAcknowledgeBody(context.OperatorPrincipalId, admission.Identity,
                admission.EffectId, observed.Effect.Receipt, observed.Effect.OriginalResult, observed.GrantId,
                observed.Effect.BlobAuthority),
            requestExpiry, null, null, work, cancellationToken).ConfigureAwait(false);
        return ack.ControlledCommand
            ?? throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteDocumentProtocol.Unavailable);
    }
}
