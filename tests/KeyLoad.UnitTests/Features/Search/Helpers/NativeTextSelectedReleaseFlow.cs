using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextSelectedReleaseFlow
{
    private const string Root = "root";
    private const string ChangedTerm = "оновлено";
    private const string ChangedJson = """{"text":"оновлено changed"}""";
    private const long Revision = 2;
    private const double Score = 1d / 61d;

    internal static async Task ExecuteAsync(TestDatabase database, NativeTextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest maintenance, SearchEngine search, CancellationToken token)
    {
        var id = Guid.NewGuid();
        _ = await NativeTextMaintenanceCommit.ExecuteAsync<ProjectionConsumerInfo>(database,
            OperationKind.ReleaseProjectionConsumer,
            new ReleaseProjectionConsumerRequest(id, maintenance.Consumer, maintenance.IndexGeneration), id, token);
        _ = await runtime.PhaseAsync(database, maintenance with { CommandId = Guid.NewGuid(), Mode = TextIndexMaintenanceMode.Release },
            TextMaintenanceCapabilityKind.Release, token: token);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var cut = database.Store.Position;
        var request = NativeTextBilingualAudit.Request(database.Partition, ChangedTerm);
        RankedDocument[]? partial = null;
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
            await search.SearchAsync(Root, request with { TextIndex = new(maintenance.Consumer, maintenance.IndexGeneration) }, token))
            ?? throw new InvalidOperationException();
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(failure.Message).IsEqualTo(NativeTextSelectedCallerFlow.Mismatch);
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        RankedDocument[] expected = [new(new(new(database.Partition, NativeTextBilingualAudit.Collection,
            NativeTextBilingualAudit.UkrainianId), Revision, ChangedJson, false, []), Score)];
        await Assert.That(JsonDefaults.Serialize(await search.SearchAsync(Root, request, token)).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
