using System.Collections.Immutable;

namespace KeyLoad.FunctionalTests.Search;

internal static class TextCapturedPublicContinuation
{
    private const int TrackedBilingualRecords = 2;
    internal static async Task<OnlineTextIndexMaintenanceResult> SwapAsync(TestDatabase fixture,
        TextOnlineTestRuntime runtime, OnlineTextIndexMaintenanceRequest original, CancellationToken token)
    {
        var receipt = fixture.Commit(new PutDocument(TextBilingualAudit.Collection,
            TextBilingualAudit.UkrainianId, """{"text":"оновлено changed"}""", ExpectedRevision: 1),
            new DeleteDocument(TextBilingualAudit.Collection, TextBilingualAudit.EnglishId, ExpectedRevision: 1));
        MutationReceipt[] expected = [new("putDocument", TextBilingualAudit.Collection, TextBilingualAudit.UkrainianId, 2),
            new("deleteDocument", TextBilingualAudit.Collection, TextBilingualAudit.EnglishId, 2)];
        await Assert.That(JsonDefaults.Serialize(receipt.Mutations).SequenceEqual(JsonDefaults.Serialize(expected.ToImmutableArray()))).IsTrue();
        foreach (var mutation in receipt.Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
        return await TextOnlineWholeFlow.RunAsync(fixture, runtime, original with
        {
            CommandId = Guid.NewGuid()
        }, token);
    }

    internal static async Task OriginalAsync(RankedDocument[] actual, TestDatabase fixture)
    {
        RankedDocument[] expected = [new(new(fixture.ExpectedReference(new(fixture.Partition, TextBilingualAudit.Collection,
            TextBilingualAudit.UkrainianId)), 1, TextBilingualAudit.UkrainianJson, false, []), 1d / 61d)];
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task HealthyAsync(TextOnlineTestRuntime runtime, TestDatabase fixture, CancellationToken token)
    {
        RankedDocument[] expected = [new(new(fixture.ExpectedReference(new(fixture.Partition, TextBilingualAudit.Collection,
            TextBilingualAudit.UkrainianId)), 2, """{"text":"оновлено changed"}""", false, []), 1d / 61d)];
        var actual = await runtime.Search.SearchAsync(TextMaintenanceTestValues.Principal,
            TextBilingualAudit.Request(fixture.Partition, "CHANGED"), token);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(await runtime.Search.SearchAsync(TextMaintenanceTestValues.Principal,
            TextBilingualAudit.Request(fixture.Partition, "ПРИВІТ"), token)).IsEmpty();
        await Assert.That(await runtime.Search.SearchAsync(TextMaintenanceTestValues.Principal,
            TextBilingualAudit.Request(fixture.Partition, "hello"), token)).IsEmpty();
    }

    internal static async Task HealthyReplayAsync(TestDatabase fixture, TextOnlineTestRuntime runtime,
        OnlineTextIndexMaintenanceRequest request, OnlineTextIndexMaintenanceResult original,
        OnlineTextIndexMaintenanceResult successor, CancellationToken token)
    {
        await Assert.That(successor.CommandId).IsNotEqualTo(request.CommandId);
        await Assert.That(successor.TrackedRecords).IsEqualTo(TrackedBilingualRecords);
        await HealthyAsync(runtime, fixture, token);
        var replay = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        await HealthyAsync(runtime, fixture, token);
        var successorReplay = await TextOnlineWholeFlow.RunAsync(fixture, runtime,
            request with
            {
                CommandId = successor.CommandId
            }, token);
        await Assert.That(OrleansSerialization.Serialize(successorReplay).SequenceEqual(OrleansSerialization.Serialize(successor))).IsTrue();
        await HealthyAsync(runtime, fixture, token);
    }
}
