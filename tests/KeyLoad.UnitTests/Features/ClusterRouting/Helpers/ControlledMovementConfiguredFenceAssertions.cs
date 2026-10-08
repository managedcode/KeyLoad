using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledMovementConfiguredFenceAssertions
{
    private const string ChangedJson = "{\"state\":\"blocked\"}";
    private static readonly Guid RejectedCommandId = Guid.Parse("e56cf1ad-46c6-423c-bd0f-2dfdbd6611c0");

    internal static async Task WriteBlockedAsync(ControlledPartitionMovementNode source,
        DateTimeOffset recordedAt, CancellationToken token)
    {
        var command = new CommandRequest(RejectedCommandId, ControlledPartitionMovementCorpus.Partition,
            [new PutDocument(ControlledPartitionMovementCorpus.Collection, ControlledPartitionMovementCorpus.DocumentId,
                ChangedJson, ControlledPartitionMovementCorpus.InitialRevision, ExplicitReplacement: true)]);
        var operation = source.Database.CreateNativeOperation(OperationKind.Batch, RejectedCommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, source.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(command));
        var rejected = source.Journal.Submit(operation, token);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(rejected.Json).IsNull();
        await Assert.That(rejected.NativeValue).IsNull();
        await Assert.That(rejected.SafeDetail).IsEqualTo(PartitionMoveProtocol.Fenced);
        var bytes = NativeSerialization.Serialize(rejected);
        var replay = source.Journal.Submit(operation, token);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(bytes)).IsTrue();
        await ControlledMovementOwnerAdmissionAssertions.RejectRestorationAsync(source);
        await ControlledPartitionMovementModelAssertions.ReadAsync(source, recordedAt, source.Store.Position, token);
    }
}
