using KeyLoad.FunctionalTests.DocumentStorage;
using P = KeyLoad.FunctionalTests.ClientApi.SdkProblemBoundaryProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal static class SdkProblemBoundaryOutbox
{
    internal static async Task RequireAsync(SdkProblemBoundaryScenario state)
    {
        var db = state.Execution.Fixture.Database;
        var seedTime = new DateTimeOffset(db.ReadStoredOutcome(state.Seed.CommandId).PreparedCommand!.Timestamp);
        var forbidden = new DocumentRecord(state.Forbidden, P.FirstRevision, P.SeedJson, new RowAccess(), seedTime);
        var allowed = new DocumentRecord(state.Allowed, P.FirstRevision, P.SeedJson, new RowAccess(), seedTime);
        var entries = new List<OutboxEntry>
        {
            new(P.FirstSequence, P.FirstOrdinal, state.SeedReceipt.Token, seedTime,
                state.Seed.Mutations[P.FirstOrdinal], state.SeedReceipt.Mutations[P.FirstOrdinal], Before: null, After: forbidden),
            new(P.SecondSequence, P.SecondOrdinal, state.SeedReceipt.Token, seedTime,
                state.Seed.Mutations[P.SecondOrdinal], state.SeedReceipt.Mutations[P.SecondOrdinal], Before: null, After: allowed)
        };
        if (state.HealthyReceipt is { } healthy)
        {
            var time = new DateTimeOffset(db.ReadStoredOutcome(state.Healthy.CommandId).PreparedCommand!.Timestamp);
            var after = new DocumentRecord(state.Allowed, P.HealthyRevision, P.HealthyJson, new RowAccess(), time);
            entries.Add(new(P.HealthySequence, P.FirstOrdinal, healthy.Token, time,
                state.Healthy.Mutations[P.FirstOrdinal], healthy.Mutations[P.FirstOrdinal], Before: allowed, After: after));
        }
        var actual = DocumentCommandOutcomeTestSupport.CaptureOutbox(db.Store, db.Database, db.Partition);
        await Assert.That(actual.EntryBytes.Length).IsEqualTo(entries.Count);
        for (var ordinal = P.FirstOrdinal; ordinal < entries.Count; ordinal++)
        {
            var stored = OrleansSerialization.Deserialize<OutboxEntry>(actual.EntryBytes[ordinal]);
            await SdkProblemBoundaryAssertions.EqualAsync(entries[ordinal], stored);
            await Assert.That(stored.Commit).IsEqualTo(entries[ordinal].Commit);
            await Assert.That(stored.Mutation.GetType()).IsEqualTo(entries[ordinal].Mutation.GetType());
            await Assert.That(stored.Receipt.CompositionReferences.IsEmpty).IsTrue();
        }
        var tail = state.HealthyReceipt is null ? P.SecondSequence : P.HealthySequence;
        var expected = new OutboxHead(tail, P.FirstSequence, entries.Count, actual.EntryBytes.Sum(value => value.Length));
        await SdkProblemBoundaryAssertions.EqualAsync(expected,
            OrleansSerialization.Deserialize<OutboxHead>(actual.HeadBytes!));
        await SdkProblemBoundaryAssertions.EqualAsync(new OutboxStatus(expected, []),
            db.Database.GetOutboxStatus(P.Root, db.Partition));
    }
}
