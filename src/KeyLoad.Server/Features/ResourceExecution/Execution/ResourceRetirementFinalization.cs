using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using Microsoft.Extensions.Options;
using CoreAttempt = KeyLoad.Core.Features.ResourceExecution.ResourceRetirementAttemptValidation;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementFinalization(ResourceRetirementReads reads,
    ResourceRetirementPhaseExecution phases, ResourceOwnerInventoryAuthority inventory,
    IOptions<DatabaseLimits> limits, TimeProvider clock)
{
    internal async Task<ResourceRetirementStateV1> ExecuteAsync(ResourceRetirementStateV1 state,
        RegisteredPhysicalOwnerV1 source, int pageSize, DateTimeOffset expiry, string principalId, long epoch,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        if (state.OwnerReceipts.Length != state.Control.Directory.Owners.Length)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        }
        var context = CoreAttempt.Seal(state.Control);
        var proof = new ResourceRetirementPreparedOperationProof(limits);
        var operations = new ResourceRetirementPreparedOperations(phases, proof);
        ResourceRetirementPreparedOperationV1? closed = null;
        if (state.SealAttempt is { } previous)
        {
            var recovery = new ResourceRetirementAttemptRecovery(new(reads), proof);
            var retained = await recovery.ReadAsync(context, previous, source, source, principalId, epoch,
                expiry, work, metadata, token).ConfigureAwait(false);
            if (retained?.Stage == ResourceRetirementPreparedOperationStage.Closed)
            {
                closed = retained;
            }
            else if (previous.ExpiresAt <= clock.GetUtcNow())
            {
                closed = await operations.CloseAsync(context, previous, source, principalId, epoch,
                    expiry, work, metadata, token).ConfigureAwait(false);
            }
            else
            {
                return await SubmitAsync(RequireResumable(retained, previous), previous, source, expiry,
                    principalId, epoch, work, metadata, token).ConfigureAwait(false);
            }
        }
        var attempt = await new ResourceRetirementAttemptSource(phases, proof, limits).PrepareAsync(context,
            state.SealAttempt, closed, source, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        var prepared = await CaptureAndPersistAsync(state.Control, attempt, source, pageSize, expiry,
            principalId, epoch, operations, work, metadata, token).ConfigureAwait(false);
        return await SubmitAsync(prepared, attempt, source, expiry, principalId, epoch, work, metadata, token).ConfigureAwait(false);
    }

    private async Task<ResourceRetirementPreparedOperationV1> CaptureAndPersistAsync(ResourceRetirementControlV1 control,
        ResourceRetirementAttemptV1 attempt, RegisteredPhysicalOwnerV1 source, int pageSize, DateTimeOffset expiry,
        string principalId, long epoch, ResourceRetirementPreparedOperations operations, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var captured = await reads.ExecuteAsync(ResourceRetirementReadOperation.CaptureRetiringInventory,
            control.Request, control, source, attempt.OriginalCommandId, pageSize, expiry,
            principalId, epoch, work, metadata, token, attempt).ConfigureAwait(false);
        var finalInventory = await inventory.ExecuteRetiringAsync(principalId, epoch, control,
            attempt, captured.InventoryControl!, work, token).ConfigureAwait(false);
        var body = new ResourceRetirementSealBodyV1(control, finalInventory) { Attempt = attempt };
        var original = await phases.IssueSealAsync(attempt, body, source, principalId, epoch,
            expiry, work, metadata, token).ConfigureAwait(false);
        var prepared = await operations.PersistAsync(CoreAttempt.Seal(control), attempt, original, source,
            principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        var retained = ResourceRetirementPreparedSealValidation.Require(prepared, control, attempt, source.Owner);
        if (!ResourceRetirementReceiptValidation.Same(retained, body))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        return prepared;
    }

    private async Task<ResourceRetirementStateV1> SubmitAsync(ResourceRetirementPreparedOperationV1 prepared,
        ResourceRetirementAttemptV1 attempt, RegisteredPhysicalOwnerV1 source, DateTimeOffset expiry,
        string principalId, long epoch, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        expiry = expiry <= attempt.ExpiresAt ? expiry : attempt.ExpiresAt;
        work.ConstrainLifetime(expiry);
        var control = prepared.Context.Retirement!;
        var body = ResourceRetirementPreparedSealValidation.Require(prepared, control, attempt, source.Owner);
        var result = await phases.SubmitOriginalAsync(prepared, source, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        var completed = ResourceRetirementReceiptValidation.RequireSourcePhase(result, control,
            ResourceRetirementPhase.SealBarrier, source, source.Owner.PhysicalShardId, attempt.AttemptId);
        if (completed.Stage != ResourceRetirementStage.BarrierSealed
            || !ResourceRetirementReceiptValidation.Same(result.Receipt, completed.SealedReceipt)
            || !ResourceRetirementReceiptValidation.Same(completed.FinalInventory, body.Inventory)
            || completed.SealAttempt is not { } sealedAttempt || sealedAttempt.OriginalFingerprint != prepared.Fingerprint
            || !ResourceRetirementAttemptValidation.SameAdmission(attempt, sealedAttempt))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        work.CheckResult(completed);
        return completed;
    }
    private static ResourceRetirementPreparedOperationV1 RequireResumable(
        ResourceRetirementPreparedOperationV1? prepared, ResourceRetirementAttemptV1 attempt)
    {
        if (prepared is null || attempt.Stage != ResourceRetirementAttemptStage.Prepared)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        }
        return prepared;
    }

}
