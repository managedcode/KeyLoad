using System.Collections.Immutable;
using KeyLoad.Query;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Search;

internal static class FilteredPolicyColdContinuation
{
    private const int ImageRecordLimit = 4096;
    private const string TextReadGrant = "filtered.search.text.read";
    private const string VectorReadGrant = "filtered.search.vector.read";
    private const string EligibleId = "a";
    private const string UnknownId = "missing";
    private const string ExpectedJson = "{\"text\":\"alpha alpha\"}";
    private const double ExpectedScore = 1d / 14d;
    private const int OneResult = 1;

    internal static async Task<(long Position, KeyValueRecord[] Records)> CaptureAsync(ZoneTreeStore store)
    {
        var captured = store.Read(view => (store.Position, Page: view.Scan([], ImageRecordLimit)));
        await Assert.That(captured.Page.HasMore).IsFalse();
        return (captured.Position, captured.Page.Records.ToArray());
    }

    internal static async Task UnchangedAsync(ZoneTreeStore store,
        (long Position, KeyValueRecord[] Records) expected)
    {
        var actual = await CaptureAsync(store);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Records.Length).IsEqualTo(expected.Records.Length);
        for (var index = 0; index < expected.Records.Length; index++)
        {
            await Assert.That(actual.Records[index].Key.Span
                .SequenceEqual(expected.Records[index].Key.Span)).IsTrue();
            await Assert.That(actual.Records[index].Value.Span
                .SequenceEqual(expected.Records[index].Value.Span)).IsTrue();
        }
    }

    internal static async Task RepairAndRecoverAsync(TestDatabase database, string reader,
        string revokedReader, CancellationToken cancellationToken)
    {
        FilteredSearchTestSupport.PersistReader(database, reader, Capability.VectorSearch,
        [
            FilteredSearchTestSupport.TextUseGrant, FilteredSearchTestSupport.VectorUseGrant,
            TextReadGrant, VectorReadGrant
        ]);
        var request = Request(database.Partition);
        var repaired = await CaptureAsync(database.Store);
        await HealthyAsync(database, database.Database, reader, request, cancellationToken);
        await UnchangedAsync(database.Store, repaired);
        var identity = database.Store.Identity;
        database.JoinOwnedResources();
        await ColdAsync(database, reader, revokedReader, request, identity, repaired, cancellationToken);
        await ColdAsync(database, reader, revokedReader, request, identity, repaired, cancellationToken);
    }

    private static async Task ColdAsync(TestDatabase database, string reader, string revokedReader,
        SearchRequest request, StoreIdentity identity,
        (long Position, KeyValueRecord[] Records) expected, CancellationToken cancellationToken)
    {
        using var store = new ZoneTreeStore(new(database.Directory), database.StorageExecution,
            UnitExecutionOptions.PointCacheExecution());
        await Assert.That(store.Identity.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
        await UnchangedAsync(store, expected);
        var engine = database.CreateConfiguredDatabase(store);
        var denied = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            new SearchEngine(engine, UnitExecutionOptions.QueryExecution()).SearchAsync(revokedReader,
                request with
                {
                    AllowedIds = ImmutableArray<string>.Empty
                }, cancellationToken)))!;
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await UnchangedAsync(store, expected);
        await HealthyAsync(database, engine, reader, request, cancellationToken);
        await UnchangedAsync(store, expected);
    }

    private static async Task HealthyAsync(TestDatabase provenance, DatabaseEngine database, string reader,
        SearchRequest request, CancellationToken cancellationToken)
    {
        var engine = new SearchEngine(database, UnitExecutionOptions.QueryExecution());
        var empty = await engine.SearchAsync(reader,
            request with
            {
                AllowedIds = ImmutableArray<string>.Empty
            }, cancellationToken);
        await Assert.That(empty).IsEmpty();
        var rows = await engine.SearchAsync(reader, request, cancellationToken);
        await Assert.That(rows).HasSingleItem();
        var row = rows[0];
        await Assert.That(row.Document.Reference).IsEqualTo(provenance.ExpectedReference(new(request.Partition,
            FilteredSearchTestSupport.Collection, EligibleId)));
        await Assert.That(row.Document.Revision).IsEqualTo((long)FilteredSearchTestSupport.Revision);
        await Assert.That(row.Document.Json).IsEqualTo(ExpectedJson);
        await Assert.That(row.Document.Redacted).IsFalse();
        await Assert.That(row.Document.RedactedFields).IsEmpty();
        await Assert.That(row.Score).IsEqualTo(ExpectedScore).Within(FilteredSearchTestSupport.Tolerance);
        await Assert.That(row.Explanation).IsNull();
    }

    private static SearchRequest Request(PartitionRef partition)
        => new(partition, FilteredSearchTestSupport.Collection,
            VectorField: FilteredSearchTestSupport.VectorField, Vector: [1, 0],
            Space: FilteredSearchTestSupport.Space, Limit: OneResult,
            FusionConstant: FilteredSearchTestSupport.FusionConstant,
            AllowedIds: ImmutableArray.Create(EligibleId, UnknownId, EligibleId));
}
