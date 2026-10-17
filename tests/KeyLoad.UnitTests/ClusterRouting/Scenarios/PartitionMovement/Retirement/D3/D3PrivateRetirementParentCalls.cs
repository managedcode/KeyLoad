using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Actual private native caller; borrows one original META grant and joins its real producer before returning.</summary>
internal static class D3PrivateRetirementParentCalls
{
    internal static ReplicatedOperation Issue(ControlledPartitionMovementNativeHost source,
        ResourceRetirementRequestV1 request, string principalId, ReadExecutionBudget work,
        ReadExecutionBudgetReadGrant metadata)
    {
        var phase = new ResourceRetirementPhaseCommandV1(ResourceRetirementProtocol.Version, request.RetirementId,
            ResourceRetirementPhase.PrepareRetirement, NativeSerialization.Serialize(new ResourceRetirementPrepareBodyV1(request)));
        using var selected = work.EnterReadGrant(metadata);
        return source.Database.CreateResourceRetirementOperation(ResourceRetirementIdentity.PreparationCommandId(request),
            principalId, source.Clock.GetUtcNow(), phase, work);
    }

    // This separate ordinary command uses its real public native admission life; it does not renew the private frame.
    internal static async Task<GrainOperationReply> ExecuteOrdinaryAsync(ControlledPartitionMovementNativeHost source,
        string principalId, CommandRequest command, ReadExecutionBudget work,
        ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var principal = source.Store.Read(raw => source.Database.Principal(work.CreateView(raw, metadata),
            principalId, source.Clock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var signed = source.Silo.CatalogRequestCodec().CreateCommand(requestId, principal.Id, OperationKind.Batch,
            command.CommandId, NativeSerialization.Serialize(command));
        GrainOperationReply? reply = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = source.Silo.OpenRequestContext(principal, requestId, command.CommandId, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reply = await source.Silo.ExecuteAsync(requestId, signed, command: true, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementReadScope.InvalidRequest);
    }

    internal static async Task<GrainOperationReply> ExecuteAsync(ControlledPartitionMovementNativeHost source,
        ReplicatedOperation original, DateTimeOffset expiry, ReadExecutionBudget work,
        ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var principal = source.Store.Read(raw => source.Database.Principal(work.CreateView(raw, metadata),
            original.PrincipalId, source.Clock.GetUtcNow()));
        var codec = source.Silo.CatalogRequestCodec();
        var admission = new NativeCommandMetadataAdmissionV1(NativeCommandMetadataProtocol.CurrentVersion,
            metadata.RemainingBytes, metadata.RemainingRecords, work.MaximumResultBytes);
        var requestId = Guid.NewGuid();
        var signed = codec.CreateResourceRetirementCommand(requestId, original, expiry, admission);
        GrainOperationReply? reply = null;
        var failures = new List<Exception>();
        var begun = false;
        var imported = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = source.Silo.OpenRequestContext(principal, requestId, original.Id, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                try
                {
                    begun = true;
                    reply = await source.Silo.ExecuteAsync(requestId, signed, command: true, token);
                    NativeCommandMetadataReplies.Require(reply, admission, codec.ExecutionOptions);
                    work.ImportSettledReadGrant(metadata, reply.Metadata!.ReadBytes, reply.Metadata.ExaminedRecords);
                    imported = true;
                }
                catch (Exception originalFailure)
                {
                    if (begun && !imported)
                    {
                        work.FailIndeterminateMetadata(originalFailure);
                    }

                    throw;
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementReadScope.InvalidRequest);
    }
}
