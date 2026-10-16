using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Publishes the exact original page once through the existing prepared-operation protocol.</summary>
internal static class NativeDropTablePageSequence
{

    internal static async Task<ResourceRetirementCleanupCheckpointV1> ExecuteAsync(NativeDropTableContext context,
        ResourceRetirementStateV1 immutable, RegisteredPhysicalOwnerV1 receiver, PartitionRef partition,
        ResourceRetirementCleanupCheckpointV1? previous)
    {
        var scope = new ResourceRetirementAttemptContextV1(ResourceRetirementAttemptPurpose.CleanupPage,
            immutable.Control, null, receiver)
        {
            Page = new(partition, previous is null ? ResourceRetirementNativeProtocol.Version
                : checked(previous.Ordinal + ResourceRetirementNativeProtocol.Version))
        };
        var proof = new ResourceRetirementPreparedOperationProof(context.Options.Core.DatabaseLimits);
        var operations = new ResourceRetirementPreparedOperations(context.Phases, proof);
        var selected = await SelectAsync(context, scope, proof, operations).ConfigureAwait(false);
        var attempt = selected.Attempt;
        var prepared = selected.Prepared;
        ReadExecutionBudgetReadGrant? dataGrant = null;
        ResourceRetirementCleanupCheckpointV1? checkpoint = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                if (prepared is null)
                {
                    var bytes = context.Work.RemainingReadGrantBytes;
                    var records = context.Work.RemainingReadGrantRecords;
                    dataGrant = context.Work.CreateReadGrant(bytes, records);
                    var body = new ResourceRetirementCleanupPageBodyV1(immutable, receiver, partition, previous)
                    {
                        Attempt = attempt,
                        MaximumReadBytes = bytes,
                        MaximumExaminedRecords = records
                    };
                    var original = await NativeDropTablePageAttempts.IssueAsync(context, scope, body).ConfigureAwait(false);
                    prepared = await operations.PersistAsync(scope, attempt, original, receiver, context.PrincipalId,
                        context.PolicyEpoch, context.Expiry <= attempt.ExpiresAt ? context.Expiry : attempt.ExpiresAt,
                        context.Work, context.Metadata, context.Token).ConfigureAwait(false);
                }
                else
                {
                    var phase = NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(prepared.Original!);
                    var originalBody = NativeSerialization.Deserialize<ResourceRetirementCleanupPageBodyV1>(phase.Body.Span);
                    dataGrant = context.Work.CreateReadGrant(originalBody.MaximumReadBytes, originalBody.MaximumExaminedRecords);
                }
                checkpoint = await NativeDropTableCleanupPageSubmission.ExecuteAsync(context, prepared, dataGrant).ConfigureAwait(false);
            }
            catch (Exception primary) when (NativeCqrsBoundaryErrors.IsNonFatal(primary))
            {
                await NativeDropTablePageFailure.ObserveAsync(context, scope, attempt, operations, primary).ConfigureAwait(false);
                throw;
            }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() =>
        {
            if (dataGrant is not null)
            {
                context.Work.CompleteSettledReadGrant(dataGrant);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return checkpoint ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
    }

    private static async Task<(ResourceRetirementAttemptV1 Attempt, ResourceRetirementPreparedOperationV1? Prepared)>
        SelectAsync(NativeDropTableContext context, ResourceRetirementAttemptContextV1 scope,
            ResourceRetirementPreparedOperationProof proof, ResourceRetirementPreparedOperations operations)
    {
        var current = await NativeDropTablePageAttempts.CurrentAsync(context, scope).ConfigureAwait(false);
        ResourceRetirementPreparedOperationV1? closed = null;
        if (current is not null)
        {
            var recovery = new ResourceRetirementAttemptRecovery(new(context.Reads), proof);
            var prepared = await recovery.ReadAsync(scope, current, context.Source, scope.Receiver!, context.PrincipalId,
                context.PolicyEpoch, context.Expiry, context.Work, context.Metadata, context.Token).ConfigureAwait(false);
            if (prepared?.Stage == ResourceRetirementPreparedOperationStage.Closed)
            {
                closed = prepared;
            }
            else if (current.ExpiresAt <= context.Clock.GetUtcNow())
            {
                closed = await operations.CloseAsync(scope, current, scope.Receiver!, context.PrincipalId,
                    context.PolicyEpoch, context.Expiry, context.Work, context.Metadata, context.Token).ConfigureAwait(false);
            }
            else
            {
                return (current, prepared);
            }
        }
        var attempt = await new ResourceRetirementAttemptSource(context.Phases, proof, context.Options.Core.DatabaseLimits)
            .PrepareAsync(scope, current, closed, context.Source, context.PrincipalId, context.PolicyEpoch, context.Expiry,
                context.Work, context.Metadata, context.Token).ConfigureAwait(false);
        return (attempt, null);
    }
}
