using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementQuiescenceData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescencePrepared
{
    internal static async Task<ResourceRetirementPreparedOperationV1> PersistAsync(ResourceRetirementQuiescenceState state,
        ControlledPartitionMovementNode owner, ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt,
        ReplicatedOperation original, CancellationToken token)
    {
        var result = await SubmitAsync(state, owner, context, attempt, ResourceRetirementPreparedOperationAction.Persist, original, token);
        var actual = result.PreparedOperation ?? throw new InvalidOperationException(Invalid);
        var native = state.Originals.Last().Original;
        var authority = new ResourceRetirementPreparedReceiptAuthorityV1(Root, ResourceRetirementQuiescencePhases.Epoch(owner),
            NativeOperationFingerprint.Compute(native), native.EvaluatedAt);
        var expected = new ResourceRetirementPreparedOperationV1(context, attempt, original,
            NativeOperationFingerprint.Compute(original), ResourceRetirementPreparedOperationStage.Persisted,
            result.Receipt, result.Receipt, null, null)
        {
            ReceiptAuthority = authority,
            PersistAuthority = authority
        };
        await ResourceRetirementQuiescenceAssertions.EqualAsync(expected, actual);
        await ResourceRetirementQuiescenceAssertions.EqualAsync(expected, Read(owner, context, attempt, token));
        return actual;
    }

    internal static ResourceRetirementPreparedOperationV1 Read(ControlledPartitionMovementNode owner,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt, CancellationToken token)
        => owner.Database.ReadPreparedResourceRetirementOperation(Root, context, attempt,
            ResourceRetirementQuiescencePhases.Work(owner, token));

    internal static async Task<ResourceRetirementPreparedOperationV1> CloseAsync(ResourceRetirementQuiescenceState state,
        ControlledPartitionMovementNode owner, ResourceRetirementPreparedOperationV1 persisted,
        OperationResult failure, CancellationToken token)
    {
        var result = await SubmitAsync(state, owner, persisted.Context, persisted.Attempt,
            ResourceRetirementPreparedOperationAction.Close, null, token);
        var original = state.Originals.Last().Original;
        var expected = persisted with
        {
            Stage = ResourceRetirementPreparedOperationStage.Closed,
            Receipt = result.Receipt,
            Error = failure.Error,
            SafeDetail = failure.SafeDetail,
            ReceiptAuthority = new(Root, ResourceRetirementQuiescencePhases.Epoch(owner), NativeOperationFingerprint.Compute(original), original.EvaluatedAt)
        };
        var actual = result.PreparedOperation ?? throw new InvalidOperationException(Invalid);
        await ResourceRetirementQuiescenceAssertions.EqualAsync(expected, actual);
        await ResourceRetirementQuiescenceAssertions.EqualAsync(expected,
            Read(owner, persisted.Context, persisted.Attempt, token));
        return actual;
    }

    private static Task<ResourceRetirementPhaseResultV1> SubmitAsync(ResourceRetirementQuiescenceState state,
        ControlledPartitionMovementNode owner, ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt,
        ResourceRetirementPreparedOperationAction action, ReplicatedOperation? original, CancellationToken token)
        => ResourceRetirementQuiescencePhases.SubmitAsync(state, owner, ResourceRetirementPhase.PreparedOperation,
            ResourceRetirementIdentity.PreparedOperationCommandId(context, attempt.AttemptId, action),
            new ResourceRetirementPreparedOperationBodyV1(context, attempt, action, original), token);
}
