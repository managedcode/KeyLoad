using KeyLoad.UnitTests.DocumentStorage;
using KeyLoad.UnitTests.Messaging;
using KeyLoad.UnitTests.OrleansExecution;
using ManagedCode.Communication;
using static KeyLoad.UnitTests.ClientApi.DeclaredResponseEofProtocol;

namespace KeyLoad.UnitTests.ClientApi;

internal static class DeclaredResponseEofAssertions
{
    internal static Task EmptyAsync(ConnectionScenario scenario)
        => EqualAsync(new OutboxStatus(new(Empty, FirstSequence, Empty, Empty), []),
            scenario.Fixture.Database.Database.GetOutboxStatus(Root, scenario.Fixture.Database.Partition));

    internal static async Task<CommitReceipt> FirstAsync(ConnectionScenario scenario,
        PrincipalRecord principal, CommandRequest command, CommitReceipt? original,
        AtomicPartitionPlacementResolution placement)
    {
        var database = scenario.Fixture.Database;
        var entry = database.ReadStoredOutcome(command.CommandId);
        var operation = entry.PreparedCommand ?? throw new InvalidOperationException(Failure);
        await Assert.That(operation.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(operation.Value!.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(operation.UserId!).IsEqualTo(principal.Id);
        await EqualAsync(command, OrleansSerialization.Deserialize<CommandRequest>((operation).Value!.Input.Span));
        var expected = new CommitReceipt(command.CommandId, new(placement.Incarnation, command.Partition.AtomicPartitionId, entry.CanonicalSequence), [new(PutKind, ConnectionProtocol.Collection, Document, Revision)], DurabilityProfile.ProcessDurable, 2);
        await EqualAsync(expected, original);
        await Assert.That(original!.Mutations.All(value => value.CompositionReferences.IsEmpty)).IsTrue();
        await DocumentAsync(scenario);
        var outbox = DocumentCommandOutcomeTestSupport.CaptureOutbox(database.Store, database.Database, database.Partition);
        await Assert.That(outbox.EntryBytes.Length).IsEqualTo(OneEntry);
        var after = new DocumentRecord(scenario.Reference(Document), Revision, OriginalJson, new RowAccess(), new DateTimeOffset(operation.Timestamp));
        var expectedEntry = new OutboxEntry(FirstSequence, OriginalOrdinal, expected.Token, new DateTimeOffset(operation.Timestamp),
            command.Mutations[OriginalOrdinal], expected.Mutations[OriginalOrdinal], Before: null, After: after);
        var payload = outbox.EntryBytes[OriginalOrdinal];
        var actual = OrleansSerialization.Deserialize<OutboxEntry>(payload);
        await EqualAsync(expectedEntry, actual);
        await Assert.That(actual.Receipt.CompositionReferences.IsEmpty).IsTrue();
        var head = new OutboxHead(FirstSequence, FirstSequence, OneEntry, payload.Length);
        await EqualAsync(head, OrleansSerialization.Deserialize<OutboxHead>(outbox.HeadBytes!));
        await EqualAsync(new OutboxStatus(head, []), database.Database.GetOutboxStatus(Root, database.Partition));
        return expected;
    }

    internal static DeclaredResponseEofCut Capture(ConnectionScenario scenario, PrincipalRecord principal,
        CommandRequest command)
    {
        var database = scenario.Fixture.Database;
        var outcome = database.Store.Read(view => view.ReadOwnedValue(
            OutcomeStoreOracle.PartitionKey(command.Partition, principal.Id, command.CommandId)))
            ?? throw new InvalidOperationException(Failure);
        return new(QueueWholeFlowStorage.Bytes(database.Store), database.Store.Position, database.ReadCanonicalCut(),
            DocumentCommandOutcomeTestSupport.CaptureOutbox(database.Store, database.Database, database.Partition), outcome);
    }

    internal static async Task CurrentAsync(ConnectionScenario scenario, PrincipalRecord principal,
        CommandRequest command, DeclaredResponseEofCut original)
    {
        await DocumentAsync(scenario);
        var actual = Capture(scenario, principal, command);
        await Assert.That(actual.Rows.SequenceEqual(original.Rows)).IsTrue();
        await Assert.That(actual.PhysicalPosition).IsEqualTo(original.PhysicalPosition);
        await Assert.That(actual.Cut).IsEqualTo(original.Cut);
        await Assert.That(actual.Outcome.SequenceEqual(original.Outcome)).IsTrue();
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxEqual(actual.Outbox, original.Outbox)).IsTrue();
    }

    internal static Task DocumentAsync(ConnectionScenario scenario)
        => EqualAsync(new DocumentResult(scenario.Reference(Document), Revision, OriginalJson, false, []), scenario.Stored(Document));

    internal static Task ProblemAsync(Problem? actual, ErrorCode code, string detail)
        => EqualAsync(Errors.Problem(code, detail), actual);

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(expected).SequenceEqual(JsonDefaults.Serialize(actual))).IsTrue();
}
