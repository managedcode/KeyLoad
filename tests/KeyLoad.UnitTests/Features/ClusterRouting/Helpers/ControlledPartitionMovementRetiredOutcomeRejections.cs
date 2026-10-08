using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises actual retired original receipt identity rejection before healthy immutable replay.</summary>
internal static class ControlledPartitionMovementRetiredOutcomeRejections
{
    private const string ChangedDocumentJson = "{\"text\":\"changed\",\"state\":\"forbidden-replay\"}";
    private const long ChangedEpochStep = 1;

    internal static async Task AssertAsync(ControlledPartitionMovementNode source, byte[] originalReceipt,
        DateTimeOffset originalRecordedAt, CancellationToken cancellationToken)
    {
        var original = ControlledPartitionMovementCorpus.Seed();
        var changedBody = original with
        {
            Mutations = [new PutDocument(ControlledPartitionMovementCorpus.Collection,
            ControlledPartitionMovementCorpus.DocumentId, ChangedDocumentJson), .. original.Mutations.Skip(1)]
        };
        var changedEpoch = original with { OwnershipEpoch = checked(original.OwnershipEpoch + ChangedEpochStep) };
        var before = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        var index = source.Journal.Log.State.LastIndex;
        var bodyDenied = source.Database.ResolveOutcome(Operation(source.Database, changedBody, originalRecordedAt));
        var epochDenied = source.Database.ResolveOutcome(Operation(source.Database, changedEpoch, originalRecordedAt));
        await Assert.That(bodyDenied.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(epochDenied.Error).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(bodyDenied.NativeValue).IsNull();
        await Assert.That(epochDenied.NativeValue).IsNull();
        await ControlledPartitionMovementReceiptAssertions.ReplayAsync(source.Journal.Submit(
            ControlledPartitionMovementCorpus.SeedOperation(source.Database, originalRecordedAt), cancellationToken), originalReceipt);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store)
            .SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
    }

    private static ReplicatedOperation Operation(DatabaseEngine database, CommandRequest command, DateTimeOffset evaluatedAt)
        => database.CreateNativeOperation(OperationKind.Batch, command.CommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, evaluatedAt, NativeSerialization.Serialize(command));
}
