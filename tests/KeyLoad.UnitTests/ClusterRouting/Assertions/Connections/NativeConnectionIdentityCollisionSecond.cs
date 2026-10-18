using KeyLoad.UnitTests.Features.ClientApi;
using KeyLoad.UnitTests.Features.DocumentStorage;
using D = KeyLoad.UnitTests.Features.ClientApi.DeclaredResponseEofProtocol;
using P = KeyLoad.UnitTests.Features.ClusterRouting.NativeConnectionIdentityCollisionProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeConnectionIdentityCollisionSecond
{
    internal static async Task<CommitReceipt> RequireAsync(NativeConnectionIdentityCollisionState state,
        AtomicPartitionPlacementResolution placement, CommitReceipt first, CommitReceipt actual)
    {
        var database = state.Scenario.Fixture.Database;
        var entry = database.ReadStoredOutcome(state.Following.CommandId);
        var operation = entry.PreparedCommand ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(operation.CommandId).IsEqualTo(state.Following.CommandId);
        await Assert.That(operation.Value!.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(operation.UserId!).IsEqualTo(state.Second.Id);
        await DeclaredResponseEofAssertions.EqualAsync(state.Following, NativeSerialization.Deserialize<CommandRequest>((operation).Value!.Input.Span));
        var expected = new CommitReceipt(state.Following.CommandId, new(placement.Incarnation, state.Following.Partition.AtomicPartitionId, entry.CanonicalSequence), [new(D.PutKind, ConnectionNativeProtocol.Collection, P.SecondDocument, P.InitialRevision)], DurabilityProfile.ProcessDurable, 1);
        await DeclaredResponseEofAssertions.EqualAsync(expected, actual);
        await Assert.That(actual.Mutations.All(value => value.CompositionReferences.IsEmpty)).IsTrue();
        await DocumentsAsync(state);
        await OutboxAsync(state, first, expected, new DateTimeOffset(operation.Timestamp));
        return expected;
    }

    private static async Task OutboxAsync(NativeConnectionIdentityCollisionState state, CommitReceipt first,
        CommitReceipt second, DateTimeOffset now)
    {
        var database = state.Scenario.Fixture.Database;
        var original = database.ReadStoredOutcome(state.Original.CommandId).PreparedCommand
            ?? throw new InvalidOperationException(P.Failure);
        OutboxEntry[] expected =
        [
            new(D.FirstSequence, P.OriginalOrdinal, first.Token, new DateTimeOffset(original.Timestamp),
                state.Original.Mutations[P.OriginalOrdinal], first.Mutations[P.OriginalOrdinal], null,
                new(state.Scenario.Reference(D.Document), D.Revision, D.OriginalJson, new RowAccess(), new DateTimeOffset(original.Timestamp))),
            new(P.Two, P.OriginalOrdinal, second.Token, now,
                state.Following.Mutations[P.OriginalOrdinal], second.Mutations[P.OriginalOrdinal], null,
                new(state.Scenario.Reference(P.SecondDocument), P.InitialRevision, P.SecondJson, new RowAccess(), now))
        ];
        var outbox = DocumentCommandOutcomeTestSupport.CaptureOutbox(database.Store, database.Database, database.Partition);
        await Assert.That(outbox.EntryBytes.Length).IsEqualTo(P.Two);
        long bytes = P.Empty;
        for (var ordinal = P.OriginalOrdinal; ordinal < P.Two; ordinal++)
        {
            var payload = outbox.EntryBytes[ordinal];
            var actual = NativeSerialization.Deserialize<OutboxEntry>(payload);
            await DeclaredResponseEofAssertions.EqualAsync(expected[ordinal], actual);
            await Assert.That(actual.Receipt.CompositionReferences.IsEmpty).IsTrue();
            bytes = checked(bytes + payload.Length);
        }
        var head = new OutboxHead(P.Two, D.FirstSequence, P.Two, bytes);
        await DeclaredResponseEofAssertions.EqualAsync(head, NativeSerialization.Deserialize<OutboxHead>(outbox.HeadBytes!));
        await DeclaredResponseEofAssertions.EqualAsync(new OutboxStatus(head, []),
            database.Database.GetOutboxStatus(D.Root, database.Partition));
    }

    internal static async Task DocumentsAsync(NativeConnectionIdentityCollisionState state)
    {
        await DeclaredResponseEofAssertions.DocumentAsync(state.Scenario);
        await DeclaredResponseEofAssertions.EqualAsync(new DocumentResult(state.Scenario.Reference(P.SecondDocument),
            P.InitialRevision, P.SecondJson, false, []), state.Scenario.Stored(P.SecondDocument));
    }
}
