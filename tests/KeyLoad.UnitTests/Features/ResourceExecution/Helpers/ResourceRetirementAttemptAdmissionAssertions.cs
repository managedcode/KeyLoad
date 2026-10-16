using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementBarrierData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementAttemptAdmissionAssertions
{
    internal static async Task OriginalAsync(ControlledPartitionMovementNode source, ResourceRetirementAttemptContextV1 context,
        ReplicatedOperation original, OperationResult result, ResourceRetirementAttemptV1 expected, CancellationToken token)
    {
        var control = context.Retirement!;
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(result, source.Journal.Submit(original, token));
        var work = ResourceRetirementPreparationValidationFixture.Work(source, token);
        var retained = source.Database.ReadResourceRetirementAttempt(Root, context, expected.AttemptId, work);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(expected, retained);
        await Assert.That(work.ReadBytes > Zero).IsTrue();
        var current = source.Database.ReadResourceRetirementState(Root, control.Request,
            ResourceRetirementPreparationValidationFixture.Work(source, token));
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(expected, current.SealAttempt!);
        await Assert.That(current.Stage).IsEqualTo(ResourceRetirementStage.Retiring);
        await Assert.That(current.FinalInventory).IsNull();
        await Assert.That(current.SealedReceipt).IsNull();
        var image = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var cut = source.Journal.Log.State.LastIndex;
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => source.Database.ReadResourceRetirementAttempt(
            Denied, context, expected.AttemptId, ResourceRetirementPreparationValidationFixture.Work(source, token)));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store)
            .SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(cut);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ResourceRetirementIdentity.PhaseCommandId(
            control.Request, ResourceRetirementPhase.SealBarrier, source.PhysicalOwner.PhysicalShardId)).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ResourceRetirementIdentity.AttemptCommandId(
            control.Request, ResourceRetirementPhase.Begin, expected.AttemptId, source.PhysicalOwner.PhysicalShardId)).Code)
            .IsEqualTo(ErrorCode.Validation);
    }
}
