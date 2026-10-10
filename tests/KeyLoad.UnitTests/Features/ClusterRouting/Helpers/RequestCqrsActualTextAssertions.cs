using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.Search;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsActualTextAssertions
{
    private const string Principal = "root";
    private const string UkrainianQuery = "ПРИВІТ";
    private const string ChangedQuery = "оновлено";
    private const string ChangedJson = "{\"text\":\"оновлено changed\"}";
    private const long OriginalRevision = 1;
    private const long ChangedRevision = 2;
    private const double Score = 1d / 61d;

    internal static Task VerifyAsync(RequestCqrsClusterFixture fixture,
        TextIndexMaintenanceRequest request, bool changed, CancellationToken token)
        => VerifyAsync(fixture, request, changed, ChangedRevision, token);

    internal static async Task VerifyAsync(RequestCqrsClusterFixture fixture,
        TextIndexMaintenanceRequest request, bool changed, long changedRevision, CancellationToken token)
    {
        var runtime = fixture.NativeMaintenance ?? throw new InvalidOperationException();
        var database = fixture.Database;
        var position = database.Store.Position;
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var projection = new NativeTextSelectedProjection(NativeTextBilingualAudit.Open(database),
                runtime.Owner, runtime.Owner);
            var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
            var selection = new TextIndexSelectionV1(request.Consumer, request.IndexGeneration);
            if (changed)
            {
                await LiteralAsync(search, database.Partition, selection, ChangedQuery,
                    NativeTextBilingualAudit.UkrainianId, ChangedJson, changedRevision, token);
                await Assert.That(await search.SearchAsync(Principal,
                    NativeTextBilingualAudit.Request(database.Partition, NativeTextBilingualAudit.EnglishQuery)
                        with
                    { TextIndex = selection }, token)).IsEmpty();
                await Assert.That(await search.SearchAsync(Principal,
                    NativeTextBilingualAudit.Request(database.Partition, UkrainianQuery)
                        with
                    { TextIndex = selection }, token)).IsEmpty();
            }
            else
            {
                await LiteralAsync(search, database.Partition, selection, UkrainianQuery,
                    NativeTextBilingualAudit.UkrainianId, NativeTextBilingualAudit.UkrainianJson, OriginalRevision, token);
                await LiteralAsync(search, database.Partition, selection, NativeTextBilingualAudit.EnglishQuery,
                    NativeTextBilingualAudit.EnglishId, NativeTextBilingualAudit.EnglishJson, OriginalRevision, token);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store).AsSpan().SequenceEqual(bytes)).IsTrue();
    }

    private static async Task LiteralAsync(SearchEngine search, PartitionRef partition,
        TextIndexSelectionV1 selection, string query, string id, string json, long revision, CancellationToken token)
    {
        var actual = await search.SearchAsync(Principal,
            NativeTextBilingualAudit.Request(partition, query) with { TextIndex = selection }, token);
        RankedDocument[] expected = [new(new(new(partition, NativeTextBilingualAudit.Collection, id),
            revision, json, false, []), Score)];
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
