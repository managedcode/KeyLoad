using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextCapturedPublicContinuation
{
    private const long TrackedBilingualRecords = 2;
    internal static async Task<OnlineTextIndexMaintenanceResult> SwapAsync(TestDatabase fixture,
        NativeTextOnlineTestRuntime runtime, OnlineTextIndexMaintenanceRequest original, CancellationToken token)
    {
        var receipt = fixture.Commit(new PutDocument(NativeTextBilingualAudit.Collection,
            NativeTextBilingualAudit.UkrainianId, """{"text":"оновлено changed"}""", ExpectedRevision: 1),
            new DeleteDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.EnglishId, ExpectedRevision: 1));
        MutationReceipt[] expected = [new("putDocument", NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId, 2),
            new("deleteDocument", NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.EnglishId, 2)];
        await Assert.That(JsonDefaults.Serialize(receipt.Mutations).SequenceEqual(JsonDefaults.Serialize(expected.ToImmutableArray()))).IsTrue();
        foreach (var mutation in receipt.Mutations)
        { await Assert.That(mutation.CompositionReferences).IsEmpty(); }
        return await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, original with { CommandId = Guid.NewGuid() }, token);
    }

    internal static async Task OriginalAsync(RankedDocument[] actual, PartitionRef partition)
    {
        RankedDocument[] expected = [new(new(new(partition, NativeTextBilingualAudit.Collection,
            NativeTextBilingualAudit.UkrainianId), 1, NativeTextBilingualAudit.UkrainianJson, false, []), 1d / 61d)];
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task HealthyAsync(NativeTextOnlineTestRuntime runtime, PartitionRef partition, CancellationToken token)
    {
        RankedDocument[] expected = [new(new(new(partition, NativeTextBilingualAudit.Collection,
            NativeTextBilingualAudit.UkrainianId), 2, """{"text":"оновлено changed"}""", false, []), 1d / 61d)];
        var actual = await runtime.Search.SearchAsync(NativeTextMaintenanceTestValues.Principal,
            NativeTextBilingualAudit.Request(partition, "CHANGED"), token);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(await runtime.Search.SearchAsync(NativeTextMaintenanceTestValues.Principal,
            NativeTextBilingualAudit.Request(partition, "ПРИВІТ"), token)).IsEmpty();
        await Assert.That(await runtime.Search.SearchAsync(NativeTextMaintenanceTestValues.Principal,
            NativeTextBilingualAudit.Request(partition, "hello"), token)).IsEmpty();
    }

    internal static async Task HealthyReplayAsync(TestDatabase fixture, NativeTextOnlineTestRuntime runtime,
        OnlineTextIndexMaintenanceRequest request, OnlineTextIndexMaintenanceResult original,
        OnlineTextIndexMaintenanceResult successor, CancellationToken token)
    {
        await Assert.That(successor.CommandId).IsNotEqualTo(request.CommandId);
        await Assert.That(successor.TrackedRecords).IsEqualTo(TrackedBilingualRecords);
        await HealthyAsync(runtime, fixture.Partition, token);
        var replay = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        await HealthyAsync(runtime, fixture.Partition, token);
        var successorReplay = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime,
            request with { CommandId = successor.CommandId }, token);
        await Assert.That(NativeSerialization.Serialize(successorReplay).SequenceEqual(NativeSerialization.Serialize(successor))).IsTrue();
        await HealthyAsync(runtime, fixture.Partition, token);
    }
}
