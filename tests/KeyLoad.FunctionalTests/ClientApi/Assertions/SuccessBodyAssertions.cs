using KeyLoad.FunctionalTests.DocumentStorage;
using KeyLoad.FunctionalTests.OrleansExecution;
using D = KeyLoad.FunctionalTests.ClientApi.DeclaredResponseEofProtocol;
using P = KeyLoad.FunctionalTests.ClientApi.SuccessBodyProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal static class SuccessBodyAssertions
{
    internal static async Task<CommitReceipt> SecondAsync(SuccessBodyState state,
        AtomicPartitionPlacementResolution placement, CommitReceipt? sdk)
    {
        var database = state.Scenario.Fixture.Database;
        var entry = database.ReadStoredOutcome(state.Endpoint.Healthy.CommandId);
        var operation = entry.PreparedCommand ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(operation.CommandId).IsEqualTo(state.Endpoint.Healthy.CommandId);
        await Assert.That(operation.Value!.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(operation.UserId!).IsEqualTo(state.Principal.Id);
        await DeclaredResponseEofAssertions.EqualAsync(state.Endpoint.Healthy,
            OrleansSerialization.Deserialize<CommandRequest>((operation).Value!.Input.Span));
        var expected = new CommitReceipt(state.Endpoint.Healthy.CommandId, new(placement.Incarnation, state.Endpoint.Healthy.Partition.AtomicPartitionId, entry.CanonicalSequence), [new(D.PutKind, ConnectionProtocol.Collection, D.Document, P.HealthyRevision)], DurabilityProfile.ProcessDurable, 2);
        await DeclaredResponseEofAssertions.EqualAsync(expected, state.Endpoint.HealthyReceipt);
        await DeclaredResponseEofAssertions.EqualAsync(expected, sdk);
        await Assert.That((state.Endpoint.HealthyReceipt ?? throw new InvalidOperationException(P.Failure))
            .Mutations.All(value => value.CompositionReferences.IsEmpty)).IsTrue();
        await DocumentSecondAsync(state);
        await OutboxSecondAsync(state, expected, new DateTimeOffset(operation.Timestamp));
        return expected;
    }

    private static async Task OutboxSecondAsync(SuccessBodyState state, CommitReceipt expected,
        DateTimeOffset now)
    {
        var database = state.Scenario.Fixture.Database;
        var original = database.ReadStoredOutcome(state.Endpoint.Original.CommandId).PreparedCommand
            ?? throw new InvalidOperationException(P.Failure);
        var firstReceipt = state.FirstReceipt ?? throw new InvalidOperationException(P.Failure);
        var before = new DocumentRecord(state.Scenario.Reference(D.Document), D.Revision, D.OriginalJson,
            new RowAccess(), new DateTimeOffset(original.Timestamp));
        var after = new DocumentRecord(state.Scenario.Reference(D.Document), P.HealthyRevision, P.HealthyJson,
            new RowAccess(), now);
        OutboxEntry[] entries =
        [
            new(D.FirstSequence, D.OriginalOrdinal, firstReceipt.Token, new DateTimeOffset(original.Timestamp),
                state.Endpoint.Original.Mutations[D.OriginalOrdinal], firstReceipt.Mutations[D.OriginalOrdinal], null, before),
            new(P.TwoEntries, D.OriginalOrdinal, expected.Token, now,
                state.Endpoint.Healthy.Mutations[D.OriginalOrdinal], expected.Mutations[D.OriginalOrdinal], before, after)
        ];
        var outbox = DocumentCommandOutcomeTestSupport.CaptureOutbox(database.Store, database.Database, database.Partition);
        await Assert.That(outbox.EntryBytes.Length).IsEqualTo(P.TwoEntries);
        long storedBytes = D.Empty;
        for (var index = P.OriginalEntry; index < P.TwoEntries; index++)
        {
            var payload = outbox.EntryBytes[index];
            var actual = OrleansSerialization.Deserialize<OutboxEntry>(payload);
            await DeclaredResponseEofAssertions.EqualAsync(entries[index], actual);
            await Assert.That(actual.Receipt.CompositionReferences.IsEmpty).IsTrue();
            storedBytes = checked(storedBytes + payload.Length);
        }
        var head = new OutboxHead(P.TwoEntries, D.FirstSequence, P.TwoEntries, storedBytes);
        await DeclaredResponseEofAssertions.EqualAsync(head, OrleansSerialization.Deserialize<OutboxHead>(outbox.HeadBytes!));
        await DeclaredResponseEofAssertions.EqualAsync(new OutboxStatus(head, []),
            database.Database.GetOutboxStatus(D.Root, database.Partition));
    }

    internal static async Task CurrentSecondAsync(SuccessBodyState state, DeclaredResponseEofCut original)
    {
        await DocumentSecondAsync(state);
        var actual = DeclaredResponseEofAssertions.Capture(state.Scenario, state.Principal, state.Endpoint.Healthy);
        await Assert.That(actual.Rows.SequenceEqual(original.Rows)).IsTrue();
        await Assert.That(actual.PhysicalPosition).IsEqualTo(original.PhysicalPosition);
        await Assert.That(actual.Cut).IsEqualTo(original.Cut);
        await Assert.That(actual.Outcome.SequenceEqual(original.Outcome)).IsTrue();
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxEqual(actual.Outbox, original.Outbox)).IsTrue();
        var first = DeclaredResponseEofAssertions.Capture(state.Scenario, state.Principal, state.Endpoint.Original);
        await Assert.That(first.Outcome.SequenceEqual((state.FirstCut
            ?? throw new InvalidOperationException(P.Failure)).Outcome)).IsTrue();
    }

    private static Task DocumentSecondAsync(SuccessBodyState state)
        => DeclaredResponseEofAssertions.EqualAsync(new DocumentResult(state.Scenario.Reference(D.Document),
            P.HealthyRevision, P.HealthyJson, false, []), state.Scenario.Stored(D.Document));
}
