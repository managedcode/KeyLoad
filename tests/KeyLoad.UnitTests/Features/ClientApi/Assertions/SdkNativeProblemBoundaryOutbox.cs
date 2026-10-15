using KeyLoad.UnitTests.Features.DocumentStorage;
using P = KeyLoad.UnitTests.Features.ClientApi.SdkNativeProblemBoundaryProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class SdkNativeProblemBoundaryOutbox
{
    internal static async Task RequireAsync(SdkNativeProblemBoundaryScenario state)
    {
        var db = state.Native.Fixture.Database;
        var seedTime = db.ReadRetainedEntry(state.Seed.CommandId).Operation!.EvaluatedAt;
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
            var time = db.ReadRetainedEntry(state.Healthy.CommandId).Operation!.EvaluatedAt;
            var after = new DocumentRecord(state.Allowed, P.HealthyRevision, P.HealthyJson, new RowAccess(), time);
            entries.Add(new(P.HealthySequence, P.FirstOrdinal, healthy.Token, time,
                state.Healthy.Mutations[P.FirstOrdinal], healthy.Mutations[P.FirstOrdinal], Before: allowed, After: after));
        }
        var actual = DocumentCommandOutcomeTestSupport.CaptureOutbox(db);
        await Assert.That(actual.EntryBytes.Length).IsEqualTo(entries.Count);
        for (var ordinal = P.FirstOrdinal; ordinal < entries.Count; ordinal++)
        {
            var stored = NativeSerialization.Deserialize<OutboxEntry>(actual.EntryBytes[ordinal]);
            await SdkNativeProblemBoundaryAssertions.EqualAsync(entries[ordinal], stored);
            await Assert.That(stored.Commit).IsEqualTo(entries[ordinal].Commit);
            await Assert.That(stored.Mutation.GetType()).IsEqualTo(entries[ordinal].Mutation.GetType());
            await Assert.That(stored.Receipt.CompositionReferences.IsEmpty).IsTrue();
        }
        var tail = state.HealthyReceipt is null ? P.SecondSequence : P.HealthySequence;
        var expected = new OutboxHead(tail, P.FirstSequence, entries.Count, actual.EntryBytes.Sum(value => value.Length));
        await SdkNativeProblemBoundaryAssertions.EqualAsync(expected,
            NativeSerialization.Deserialize<OutboxHead>(actual.HeadBytes!));
        await SdkNativeProblemBoundaryAssertions.EqualAsync(new OutboxStatus(expected, []),
            db.Database.GetOutboxStatus(P.Root, db.Partition));
    }
}
