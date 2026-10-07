using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class CanonicalVectorRejectedOutcomeAssertions
{
    internal static async Task VerifyReplayAsync(TestDatabase database, CommandRequest command,
        bool dimensionFailure, long previousPosition)
    {
        var rejected = database.Submit(OperationKind.Batch, command, id: command.CommandId);
        await Assert.That(rejected.Error).IsEqualTo(dimensionFailure ? ErrorCode.Validation : ErrorCode.RevisionConflict);
        await Assert.That(rejected.SafeDetail).IsEqualTo(dimensionFailure
            ? "The vector dimension or values are invalid." : "The expected revision does not match.");
        await Assert.That(rejected.Json).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(previousPosition + 1);
        var persisted = OutcomeBytes(database, command);
        var replayed = database.Submit(OperationKind.Batch, command, id: command.CommandId);
        await Assert.That(JsonDefaults.Serialize(replayed)).IsEquivalentTo(JsonDefaults.Serialize(rejected), CollectionOrdering.Matching);
        await Assert.That(OutcomeBytes(database, command)).IsEquivalentTo(persisted, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(previousPosition + 1);
    }

    private static byte[] OutcomeBytes(TestDatabase database, CommandRequest command)
        => database.Store.Read(view => view.ReadOwnedValue(
            OutcomeStoreOracle.PartitionKey(database.Partition, "root", command.CommandId)))
            ?? throw new InvalidOperationException("The rejected canonical vector outcome was not persisted.");
}
