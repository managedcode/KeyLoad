using System.Collections.Immutable;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class FilteredSearchPolicyTests
{
    private const string Reader = "filtered-reader";
    private const string RevokedReader = "filtered-revoked";
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string OwnedId = "owned";
    private const string HiddenId = "hidden";
    private const string StaleId = "stale";
    private const string DeletedId = "deleted";

    [Test]
    public async Task HiddenDeletedAndStaleVectorRowsNeverEscapeFilteredSearch()
    {
        using var database = FilteredSearchTestSupport.Create();
        database.Commit(
            new PutDocument(FilteredSearchTestSupport.Collection, OwnedId, "{\"text\":\"alpha\"}", Access: new(Alice)),
            new PutDocument(FilteredSearchTestSupport.Collection, HiddenId, "{\"text\":\"alpha\"}", Access: new(Bob)),
            new PutDocument(FilteredSearchTestSupport.Collection, StaleId, "{\"text\":\"alpha\"}", Access: new(Alice)),
            new PutDocument(FilteredSearchTestSupport.Collection, DeletedId, "{\"text\":\"alpha\"}", Access: new(Alice)),
            new PutVector(FilteredSearchTestSupport.Collection, OwnedId, FilteredSearchTestSupport.VectorField,
                [1, 0], FilteredSearchTestSupport.Space, FilteredSearchTestSupport.Revision),
            new PutVector(FilteredSearchTestSupport.Collection, HiddenId, FilteredSearchTestSupport.VectorField,
                [1, 0], FilteredSearchTestSupport.Space, FilteredSearchTestSupport.Revision),
            new PutVector(FilteredSearchTestSupport.Collection, StaleId, FilteredSearchTestSupport.VectorField,
                [1, 0], FilteredSearchTestSupport.Space, FilteredSearchTestSupport.Revision),
            new PutVector(FilteredSearchTestSupport.Collection, DeletedId, FilteredSearchTestSupport.VectorField,
                [1, 0], FilteredSearchTestSupport.Space, FilteredSearchTestSupport.Revision));
        database.Commit(new PatchDocument(FilteredSearchTestSupport.Collection, StaleId,
            [new(FilteredSearchTestSupport.TextField, PatchKind.Set, "\"alpha alpha\"")], FilteredSearchTestSupport.Revision));
        database.Commit(new DeleteDocument(FilteredSearchTestSupport.Collection, DeletedId,
            FilteredSearchTestSupport.Revision));
        FilteredSearchTestSupport.PersistReader(database, Reader, Capability.VectorSearch,
            [FilteredSearchTestSupport.TextUseGrant, FilteredSearchTestSupport.VectorUseGrant], Alice, restrictRows: true);

        var request = FilteredSearchTestSupport.Request(ImmutableArray.Create(OwnedId, HiddenId, StaleId, DeletedId, "absent"));
        var hybrid = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(Reader, request, Token());
        var vector = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(Reader, VectorOnly(request.AllowedIds), Token());

        await Assert.That(hybrid.Select(row => row.Document.Reference.Id).ToArray())
            .IsEquivalentTo(new[] { OwnedId, StaleId }, CollectionOrdering.Matching);
        await Assert.That(vector.Select(row => row.Document.Reference.Id).ToArray())
            .IsEquivalentTo(new[] { OwnedId }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task EmptyAllowlistStillEnforcesPersistedCapabilitiesFieldUseAndRevocation()
    {
        using var database = FilteredSearchTestSupport.Create();
        FilteredSearchTestSupport.AddCorpus(database);
        FilteredSearchTestSupport.PersistReader(database, Reader, Capability.None, []);
        FilteredSearchTestSupport.PersistReader(database, RevokedReader, Capability.VectorSearch,
            [FilteredSearchTestSupport.VectorUseGrant], revoked: true);
        var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var empty = ImmutableArray<string>.Empty;

        var capabilityFailure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            engine.SearchAsync(Reader, VectorOnly(empty), Token())))!;
        var fieldFailure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            engine.SearchAsync(Reader, FilteredSearchTestSupport.Request(empty, hybrid: false), Token())))!;
        var revokedFailure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            engine.SearchAsync(RevokedReader, VectorOnly(empty), Token())))!;

        await Assert.That(capabilityFailure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(fieldFailure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(revokedFailure.Code).IsEqualTo(ErrorCode.Unauthenticated);
    }

    private static SearchRequest VectorOnly(ImmutableArray<string>? allowedIds)
        => new(new("tenant", "database", "orders", FilteredSearchTestSupport.PartitionKey), FilteredSearchTestSupport.Collection,
            VectorField: FilteredSearchTestSupport.VectorField, Vector: [1, 0],
            Space: FilteredSearchTestSupport.Space, AllowedIds: allowedIds);

    private static CancellationToken Token() => TestContext.Current!.Execution.CancellationToken;
}
