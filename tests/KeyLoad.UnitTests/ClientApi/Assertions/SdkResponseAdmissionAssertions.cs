using ManagedCode.Communication;
using static KeyLoad.UnitTests.Features.ClientApi.SdkResponseAdmissionProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class SdkResponseAdmissionAssertions
{
    internal static async Task ProblemAsync(Problem actual, ErrorCode code, string detail)
        => await EqualAsync(actual, Errors.Problem(code, detail));

    internal static Task EmptyAsync(TestDatabase database)
        => EqualAsync(database.Database.GetOutboxStatus(Root, database.Partition),
            new OutboxStatus(new(NoEntries, FirstSequence, NoEntries, NoEntries), []));

    internal static async Task FirstAsync(TestDatabase database, SdkResponseAdmissionOwner owner)
    {
        await EqualAsync(owner.Original, owner.Expected);
        await Assert.That(owner.Original!.Mutations.All(item => item.CompositionReferences.IsEmpty)).IsTrue();
        var snapshot = KeyLoad.UnitTests.Features.DocumentStorage.DocumentCommandOutcomeTestSupport.CaptureOutbox(database);
        await Assert.That(snapshot.EntryBytes.Length).IsEqualTo(SingleEntry);
        var reference = new EntityRef(database.Partition, Collection, Document);
        var mutation = new PutDocument(Collection, Document, SdkResponseAdmissionProtocol.Json, InitialRevision);
        var effect = new MutationReceipt(PutKind, Collection, Document, CommittedRevision);
        var after = new DocumentRecord(database.ExpectedReference(reference), CommittedRevision, SdkResponseAdmissionProtocol.Json, new RowAccess(), DateTimeOffset.UnixEpoch);
        var expected = new OutboxEntry(FirstSequence, FirstOrdinal, owner.ExpectedToken,
            DateTimeOffset.UnixEpoch, mutation, effect, Before: null, After: after);
        var payload = snapshot.EntryBytes[FirstOrdinal];
        var actual = OrleansSerialization.Deserialize<OutboxEntry>(payload);
        await EqualAsync(actual, expected);
        await Assert.That(actual.Receipt.CompositionReferences.IsEmpty).IsTrue();
        var head = new OutboxHead(FirstSequence, FirstSequence, SingleEntry, payload.Length);
        await EqualAsync(OrleansSerialization.Deserialize<OutboxHead>(snapshot.HeadBytes!), head);
        await EqualAsync(database.Database.GetOutboxStatus(Root, database.Partition), new OutboxStatus(head, []));
    }

    private static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
