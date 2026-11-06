using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal static class CanonicalVectorRejectedOutcomeAssertions
{
    internal static async Task VerifyReplayAsync(TestDatabase database, CommandRequest command,
        bool dimensionFailure, long previousPosition)
    {
        var rejected = database.Submit(OperationKind.Batch, command, id: command.CommandId);
        await Assert.That(Errors.Code(rejected.Problem).Value).IsEqualTo(dimensionFailure ? ErrorCode.Validation : ErrorCode.RevisionConflict);
        await Assert.That(rejected.Problem?.Detail).IsEqualTo(dimensionFailure
            ? "The vector dimension or values are invalid." : "The expected revision does not match.");

        var committedPosition = database.Store.Position;
        await Assert.That(committedPosition).IsGreaterThan(previousPosition);
        var persisted = OutcomeBytes(database, command);
        var replayed = database.Submit(OperationKind.Batch, command, id: command.CommandId);
        await Assert.That(JsonDefaults.Serialize(replayed)).IsEquivalentTo(JsonDefaults.Serialize(rejected), CollectionOrdering.Matching);
        await Assert.That(OutcomeBytes(database, command)).IsEquivalentTo(persisted, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(committedPosition);
    }

    private static byte[] OutcomeBytes(TestDatabase database, CommandRequest command)
        => database.Store.Read(view => view.ReadOwnedValue(
            OutcomeStoreOracle.PartitionKey(database.Partition, "root", command.CommandId)))
            ?? throw new InvalidOperationException("The rejected canonical vector outcome was not persisted.");
}
