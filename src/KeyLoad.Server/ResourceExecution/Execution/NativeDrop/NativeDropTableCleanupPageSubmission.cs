using KeyLoad.Clustering;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeDropTableCleanupPageSubmission
{
    internal static async Task<ResourceRetirementCleanupCheckpointV1> ExecuteAsync(NativeDropTableContext context,
        ResourceRetirementPreparedOperationV1 prepared, ReadExecutionBudgetReadGrant originalGrant)
    {
        var work = context.Work;
        work.Check();
        await context.Client.RequireRetirementSourceAsync(context.PrincipalId, context.PolicyEpoch, work, context.Metadata, context.Token)
            .ConfigureAwait(false);
        var original = prepared.Original
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        var phase = NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(original);
        var body = NativeSerialization.Deserialize<ResourceRetirementCleanupPageBodyV1>(phase.Body.Span);
        new ResourceRetirementPreparedOperationProof(context.Options.Core.DatabaseLimits)
            .RequirePersisted(prepared, prepared.Context, prepared.Attempt);
        var expiry = context.Expiry <= prepared.Attempt.ExpiresAt ? context.Expiry : prepared.Attempt.ExpiresAt;
        if (expiry <= context.Clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        }
        if (context.Metadata.RemainingBytes <= ResourceRetirementReadContract.NoReadBytes
            || context.Metadata.RemainingRecords <= ResourceRetirementReadContract.NoRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }

        var query = new ResourceRetirementNativePhaseRequestV1(ResourceRetirementNativeProtocol.Version, phase,
            body.Receiver, string.Empty, string.Empty, ResourceRetirementReadContract.MinimumPolicyEpoch, expiry,
            context.Metadata.RemainingBytes, context.Metadata.RemainingRecords, work.MaximumResultBytes)
        {
            OriginalOperation = original
        };
        work.CheckResult(query);
        var result = await context.Client.ExecuteRetirementPhaseAsync(body.Receiver, original.Id, query,
            context.PrincipalId, context.PolicyEpoch, work, context.Metadata, context.Token).ConfigureAwait(false);
        ResourceRetirementCompletionReply.Require(phase, result, context.Options.Core.DatabaseLimits.Value);
        if (result.Receipt.CommandId != original.Id || result.CleanupCheckpoint is not { } checkpoint
            || checkpoint.AttemptId != prepared.Attempt.AttemptId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        }
        work.ImportSettledReadGrant(originalGrant, checkpoint.ExaminedBytes, checkpoint.ExaminedRecords);
        work.CheckResult(result);
        work.Check();
        return checkpoint;
    }
}
