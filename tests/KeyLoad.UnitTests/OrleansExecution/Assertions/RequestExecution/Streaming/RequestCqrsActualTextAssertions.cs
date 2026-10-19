using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.Search;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class RequestCqrsActualTextAssertions
{
    private const string Principal = "root";
    private const string UkrainianQuery = "ПРИВІТ";
    private const string ChangedQuery = "оновлено";
    private const string ChangedJson = "{\"text\":\"оновлено changed\"}";
    private const long OriginalRevision = 1;
    private const long ChangedRevision = 2;
    private const double Score = 1d / 61d;

    internal static Task VerifyAsync(RequestCqrsHostedRuntimeFixture fixture,
        TextIndexMaintenanceRequest request, ResourceLifetime lifetime, bool changed, CancellationToken token)
        => VerifyAsync(fixture, request, lifetime, changed, ChangedRevision, token);

    internal static async Task VerifyAsync(RequestCqrsHostedRuntimeFixture fixture,
        TextIndexMaintenanceRequest request, ResourceLifetime lifetime, bool changed, long changedRevision, CancellationToken token)
    {
        var runtime = fixture.RuntimeMaintenance ?? throw new InvalidOperationException();
        var database = fixture.Database;
        var position = database.Store.Position;
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var projection = new TextSelectedProjection(RequestCqrsHostedMaintenanceData.Open(database),
                runtime, runtime);
            var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
            var selection = new TextIndexSelection(request.Consumer, request.IndexGeneration);
            if (changed)
            {
                await LiteralAsync(search, database.Partition, selection, lifetime, ChangedQuery,
                    TextBilingualAudit.UkrainianId, ChangedJson, changedRevision, token);
                await Assert.That(await search.SearchAsync(Principal,
                    TextBilingualAudit.Request(database.Partition, TextBilingualAudit.EnglishQuery)
                        with
                    {
                        TextIndex = selection
                    }, token)).IsEmpty();
                await Assert.That(await search.SearchAsync(Principal,
                    TextBilingualAudit.Request(database.Partition, UkrainianQuery)
                        with
                    {
                        TextIndex = selection
                    }, token)).IsEmpty();
            }
            else
            {
                await LiteralAsync(search, database.Partition, selection, lifetime, UkrainianQuery,
                    TextBilingualAudit.UkrainianId, TextBilingualAudit.UkrainianJson, OriginalRevision, token);
                await LiteralAsync(search, database.Partition, selection, lifetime, TextBilingualAudit.EnglishQuery,
                    TextBilingualAudit.EnglishId, TextBilingualAudit.EnglishJson, OriginalRevision, token);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store).AsSpan().SequenceEqual(bytes)).IsTrue();
    }

    private static async Task LiteralAsync(SearchEngine search, PartitionRef partition,
        TextIndexSelection selection, ResourceLifetime lifetime, string query, string id, string json, long revision, CancellationToken token)
    {
        var actual = await search.SearchAsync(Principal,
            TextBilingualAudit.Request(partition, query) with
            {
                TextIndex = selection
            }, token);
        var reference = new EntityRef(partition, TextBilingualAudit.Collection, id)
        {
            Lifetime = new(lifetime.Incarnation.Value)
        };
        RankedDocument[] expected = [new(new(reference, revision, json, false, []), Score)];
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
