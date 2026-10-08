using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextSelectedGenerationFlow
{
    private const string Root = "root";
    private const long OriginalRevision = 1;
    private const long ChangedRevision = 2;
    private const string ChangedJson = "{\"text\":\"оновлено changed\"}";
    private const string ChangedQuery = "оновлено";
    private const double Score = 1d / 61d;

    internal static async Task RunAsync(TestDatabase database, NativeTextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest maintenance, CancellationToken token)
    {
        await NativeTextSelectedCallerFlow.PersistAsync(database, token);
        _ = await NativeTextMaintenancePhaseFlow.FinishAsync(database, runtime, maintenance, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var projection = new NativeTextSelectedProjection(NativeTextBilingualAudit.Open(database), runtime.Owner, runtime.Owner);
            await ServerFailureObserver.ObserveAsync(
                () => RunQueriesAsync(database, runtime, maintenance, projection, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunQueriesAsync(TestDatabase database, NativeTextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest maintenance, NativeTextSelectedProjection projection, CancellationToken token)
    {
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var selection = new TextIndexSelectionV1(maintenance.Consumer, maintenance.IndexGeneration);
        await LiteralAsync(search, database.Partition, selection, "ПРИВІТ", NativeTextBilingualAudit.UkrainianId,
            NativeTextBilingualAudit.UkrainianJson, OriginalRevision, token);
        await LiteralAsync(search, database.Partition, selection, "hello", NativeTextBilingualAudit.EnglishId,
            NativeTextBilingualAudit.EnglishJson, OriginalRevision, token);
        await NativeTextSelectedCallerFlow.VerifyAsync(database, search, selection, token);
        var id = Guid.NewGuid();
        var write = new CommandRequest(id, database.Partition,
            [new PutDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId,
                    ChangedJson, ExpectedRevision: OriginalRevision),
                 new DeleteDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.EnglishId,
                    ExpectedRevision: OriginalRevision)]);
        _ = await NativeTextMaintenanceCommit.ExecuteAsync<CommitReceipt>(database, OperationKind.Batch, write, id, token);
        await RejectAsync(database, search, selection, token);
        await runtime.Owner.AbortAsync(runtime.SessionId);
        var restore = maintenance with { CommandId = Guid.NewGuid(), Mode = TextIndexMaintenanceMode.Restore };
        _ = await NativeTextMaintenancePhaseFlow.FinishAsync(database, runtime, restore, token);
        await LiteralAsync(search, database.Partition, selection, ChangedQuery, NativeTextBilingualAudit.UkrainianId,
            ChangedJson, ChangedRevision, token);
        await Assert.That(await search.SearchAsync(Root,
            NativeTextBilingualAudit.Request(database.Partition, "hello") with { TextIndex = selection }, token)).IsEmpty();
        await NativeTextSelectedReleaseFlow.ExecuteAsync(database, runtime, restore, search, token);
    }

    private static async Task RejectAsync(TestDatabase database, SearchEngine search,
        TextIndexSelectionV1 selection, CancellationToken token)
    {
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        RankedDocument[]? partial = null;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
            await search.SearchAsync(Root, NativeTextBilingualAudit.Request(database.Partition, ChangedQuery)
                with
            { TextIndex = selection }, token)) ?? throw new InvalidOperationException();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(error.Message).IsEqualTo(NativeTextSelectedCallerFlow.Mismatch);
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }

    private static async Task LiteralAsync(SearchEngine search, PartitionRef partition, TextIndexSelectionV1 selection,
        string query, string id, string json, long revision, CancellationToken token)
    {
        var actual = await search.SearchAsync(Root, NativeTextBilingualAudit.Request(partition, query)
            with
        { TextIndex = selection }, token);
        var expected = new RankedDocument(new(new(partition, NativeTextBilingualAudit.Collection, id), revision, json, false, []), Score);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(new[] { expected }))).IsTrue();
    }
}
