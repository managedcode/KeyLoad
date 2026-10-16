using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementPhaseExecution(ResourceOwnerInventoryNativeClient client)
{
    internal async Task<ResourceRetirementPhaseResultV1> ExecuteAsync<T>(ResourceRetirementRequestV1 request,
        ResourceRetirementPhase phase, T body, RegisteredPhysicalOwnerV1 destination, Guid identityOwner,
        string principalId, long policyEpoch, DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token,
        Guid attemptId = default, ReplicatedOperation? originalOperation = null)
    {
        await client.RequireRetirementSourceAsync(principalId, policyEpoch, work, metadata, token).ConfigureAwait(false);
        var commandId = CommandId(request, phase, body, identityOwner, attemptId);
        var query = CreateRequest(request, phase, body, destination, expiry, work, metadata) with
        {
            OriginalOperation = originalOperation
        };
        RequireNativeSize(query, work);
        var result = await client.ExecuteRetirementPhaseAsync(destination, commandId, query,
            principalId, policyEpoch, work, metadata, token).ConfigureAwait(false);
        if (result.Receipt.CommandId != commandId || result.Receipt.RetirementId != request.RetirementId
            || result.Receipt.Phase != phase)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        RequireNativeSize(result, work);
        work.MeasureResult(result);
        return result;
    }

    internal async Task<ReplicatedOperation> IssueSealAsync(ResourceRetirementAttemptV1 attempt, ResourceRetirementSealBodyV1 body, RegisteredPhysicalOwnerV1 source,
        string principalId, long policyEpoch, DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var operation = await ResourceRetirementSourceSealReads.ExecuteAsync(client, body, source,
            principalId, policyEpoch, expiry, work, metadata, token).ConfigureAwait(false);
        if (operation.Id != attempt.OriginalCommandId || operation.PrincipalId != principalId || operation.NativePayload.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        RequireNativeSize(operation, work);
        work.MeasureResult(operation);
        return operation;
    }

    private static ResourceRetirementNativePhaseRequestV1 CreateRequest<T>(ResourceRetirementRequestV1 request,
        ResourceRetirementPhase phase, T body, RegisteredPhysicalOwnerV1 destination, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata)
    {
        work.Check();
        work.MeasureResult(body);
        RequireNativeSize(body, work);
        var bytes = NativeSerialization.Serialize(body);
        if (bytes.Length > work.MaximumResultBytes || metadata.RemainingBytes <= ResourceRetirementReadContract.NoReadBytes || metadata.RemainingRecords <= ResourceRetirementReadContract.NoRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
        var command = new ResourceRetirementPhaseCommandV1(ResourceRetirementNativeProtocol.Version,
            request.RetirementId, phase, bytes);
        var query = new ResourceRetirementNativePhaseRequestV1(ResourceRetirementNativeProtocol.Version, command,
            destination, string.Empty, string.Empty, ResourceRetirementReadContract.MinimumPolicyEpoch, expiry, metadata.RemainingBytes,
            metadata.RemainingRecords, work.MaximumResultBytes);
        RequireNativeSize(query, work);
        work.MeasureResult(query);
        return query;
    }

    private static void RequireNativeSize<T>(T value, ReadExecutionBudget work)
    {
        if (NativeSerialization.Measure(value) > work.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
        work.Check();
    }

    internal async Task<ResourceRetirementPhaseResultV1> SubmitOriginalAsync(ResourceRetirementPreparedOperationV1 prepared,
        RegisteredPhysicalOwnerV1 destination, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        await client.RequireRetirementSourceAsync(principalId, epoch, work, metadata, token).ConfigureAwait(false);
        if (metadata.RemainingBytes <= ResourceRetirementReadContract.NoReadBytes
            || metadata.RemainingRecords <= ResourceRetirementReadContract.NoRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }

        var original = prepared.Original ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        var command = NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(original);
        var query = new ResourceRetirementNativePhaseRequestV1(ResourceRetirementNativeProtocol.Version, command,
            destination, string.Empty, string.Empty, ResourceRetirementReadContract.MinimumPolicyEpoch, expiry, metadata.RemainingBytes,
            metadata.RemainingRecords, work.MaximumResultBytes)
        {
            OriginalOperation = original
        };
        RequireNativeSize(query, work);
        var result = await client.ExecuteRetirementPhaseAsync(destination, original.Id, query, principalId, epoch, work, metadata, token)
            .ConfigureAwait(false);
        if (result.Receipt.CommandId != original.Id || result.Receipt.Phase != command.Phase
            || result.Receipt.RetirementId != command.RetirementId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        RequireNativeSize(result, work);
        work.MeasureResult(result);
        return result;
    }

    private static Guid CommandId<T>(ResourceRetirementRequestV1 request, ResourceRetirementPhase phase,
        T body, Guid identityOwner, Guid attemptId)
    {
        if (phase == ResourceRetirementPhase.CleanupPage && body is ResourceRetirementCleanupPageBodyV1 page)
        {
            return page.Attempt?.OriginalCommandId
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        }
        if (body is ResourceRetirementPersistDropParentBodyV1 persist)
        {
            return ResourceRetirementDropParentIdentity.CommandId(persist.Lookup, phase);
        }
        if (body is ResourceRetirementCompleteDropParentBodyV1 complete)
        {
            return ResourceRetirementDropParentIdentity.CommandId(complete.Lookup, phase);
        }
        if (phase == ResourceRetirementPhase.PrepareAttempt && body is ResourceRetirementPrepareAttemptBodyV1 prepare)
        {
            return ResourceRetirementIdentity.AttemptCommandId(prepare.Context, phase, prepare.AttemptId);
        }
        if (phase == ResourceRetirementPhase.PreparedOperation && body is ResourceRetirementPreparedOperationBodyV1 prepared)
        {
            return ResourceRetirementIdentity.PreparedOperationCommandId(prepared.Context, prepared.Attempt.AttemptId, prepared.Action);
        }
        return phase is ResourceRetirementPhase.SealBarrier or ResourceRetirementPhase.ReserveOwner or ResourceRetirementPhase.RecordReservation
            ? ResourceRetirementIdentity.AttemptCommandId(request, phase, attemptId, identityOwner)
            : ResourceRetirementIdentity.PhaseCommandId(request, phase, identityOwner);
    }
}
