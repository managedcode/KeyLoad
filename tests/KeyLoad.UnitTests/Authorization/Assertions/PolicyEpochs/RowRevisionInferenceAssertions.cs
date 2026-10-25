using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using P = KeyLoad.UnitTests.Authorization.RowRevisionInferenceProtocol;

namespace KeyLoad.UnitTests.Authorization;

internal static class RowRevisionInferenceAssertions
{
    internal static async Task ResultAsync(Result<object> actual, ErrorCode code, string detail)
    {

        await Assert.That(actual.Value).IsNull();
        await Assert.That(Errors.Code(actual.Problem).Value).IsEqualTo(code);
        await Assert.That(actual.Problem?.Detail).IsEqualTo(detail);
    }

    internal static async Task<(Command<DatabaseCommand> Operation, Result<object> Result, byte[] Outcome)> RefuseAsync(
        DatabaseEngine database, ZoneTreeStore store, CommandRequest command, ErrorCode code,
        string detail, CancellationToken token)
    {
        var before = TeamRowPolicyAssertions.Image(store);
        var expectedApplied = checked(database.LastApplied + 1L);
        var beforeClock = store.Read(view => view.ReadOwnedValue(KeySpace.ClockBytes))!;
        var outbox = JsonDefaults.Serialize(database.GetOutboxStatus(P.Root, command.Partition));
        var operation = RowRevisionInferenceRequests.Operation(database, command);
        var result = database.ApplyEmbedded(operation, token);
        await ResultAsync(result, code, detail);
        var outcomeKey = KeySpace.PartitionOutcome(command.Partition, P.Reader, command.CommandId);
        var locatorKey = KeySpace.OutcomeLocator(command.Partition, P.Reader, command.CommandId);
        var bytes = store.Read(view => view.ReadOwnedValue(outcomeKey))!;
        var stored = OrleansSerialization.Deserialize<StoredOutcome>(bytes);
        await Assert.That(stored.PolicyEpoch).IsEqualTo(P.PolicyEpoch);
        await Assert.That(stored.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(JsonDefaults.Serialize(stored.Result).AsSpan().SequenceEqual(JsonDefaults.Serialize(result))).IsTrue();
        await Assert.That(store.Read(view => view.ReadOwnedValue(locatorKey))!.AsSpan().SequenceEqual(outcomeKey)).IsTrue();
        string[] allowed = [Convert.ToHexString(outcomeKey), Convert.ToHexString(locatorKey), Convert.ToHexString(KeySpace.ClockBytes), Convert.ToHexString(KeySpace.AppliedBytes), Convert.ToHexString(KeySpace.LastCommandBytes)];
        await Assert.That(Filter(before, allowed).SequenceEqual(Filter(TeamRowPolicyAssertions.Image(store), allowed))).IsTrue();
        await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes))!
            .AsSpan().SequenceEqual(OrleansSerialization.Serialize(expectedApplied))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(database.GetOutboxStatus(P.Root, command.Partition)).AsSpan().SequenceEqual(outbox)).IsTrue();
        var afterClock = store.Read(view => view.ReadOwnedValue(KeySpace.ClockBytes))!;
        await Assert.That(OrleansSerialization.Deserialize<DateTimeOffset>(afterClock))
            .IsGreaterThanOrEqualTo(OrleansSerialization.Deserialize<DateTimeOffset>(beforeClock));
        await ReplayAsync(database, store, operation, result, bytes, command.Partition, token);
        return (operation, result, bytes);
    }

    private static string[] Filter(string[] rows, string[] allowed)
        => rows.Where(row => !allowed.Any(key => row.StartsWith(key + P.Separator, StringComparison.Ordinal))).ToArray();

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        Command<DatabaseCommand> operation, Result<object> expected, byte[] outcome, PartitionRef partition,
        CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var position = store.Position;
        var actual = database.ApplyEmbedded(operation, token);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var retained = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(partition, P.Reader, operation.CommandId)))!;
        await Assert.That(retained.AsSpan().SequenceEqual(outcome)).IsTrue();
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, position);
    }

    internal static async Task DocumentAsync(TestDatabase creation, DatabaseEngine database, PartitionRef partition, string id,
        long revision, string json, CancellationToken token)
    {
        DocumentResult expected = new(creation.ExpectedReference(new(partition, P.Collection, id)), revision, json, false, []);
        var actual = database.GetDocument(P.Root, expected.Reference, cancellationToken: token);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task<(Command<DatabaseCommand> Operation, Result<object> Result, byte[] Outcome)> HealthyAsync(TestDatabase db, string kind, CancellationToken token)
    {
        var command = RowRevisionInferenceRequests.Create(db.Partition, kind, P.Hidden, P.RepairRevision);
        var operation = RowRevisionInferenceRequests.Operation(db.Database, command);
        var before = db.Store.Position;
        var expectedToken = db.NextCommitToken();
        var actual = db.Database.ApplyEmbedded(operation, token);
        var receipt = actual.Get<CommitReceipt>();
        CommitReceipt expected = new(command.CommandId, expectedToken, [new(RowRevisionInferenceRequests.ReceiptKind(kind), P.Collection, P.Hidden, P.HealthyRevision)], DurabilityProfile.ProcessDurable, 1);
        await Assert.That(JsonDefaults.Serialize(receipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(db.Store.Position).IsEqualTo(before + 1L);
        if (kind == P.Delete)
        {
            await Assert.That(db.Database.GetDocument(P.Root, new(db.Partition, P.Collection, P.Hidden), cancellationToken: token)).IsNull();
        }
        else
        {
            await DocumentAsync(db, db.Database, db.Partition, P.Hidden, P.HealthyRevision, P.Changed, token);
        }
        var outcome = db.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(db.Partition, P.Reader, command.CommandId)))!;
        await ReplayAsync(db.Database, db.Store, operation, actual, outcome, db.Partition, token);
        await DocumentAsync(db, db.Database, db.Partition, P.Own, P.OriginalRevision, P.Original, token);
        await Assert.That(db.Database.GetDocument(P.Root, new(db.Partition, P.Collection, P.Tombstone), cancellationToken: token)).IsNull();
        return (operation, actual, outcome);
    }
}
