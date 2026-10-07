using KeyLoad.Client;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class VectorAttachmentAdmissionWholeFlowTests
{
    private const string Denied = "denied-vector-reader";
    private const string Tenant = "tenant";
    private const int AttachmentItems = 1;
    private const string UnsupportedDetail = "This expression is outside the supported Q1 C# subset.";
    private const string LimitDetail = "The query vector attachment exceeds its item budget.";
    private const string DeniedDetail = "The principal cannot perform this operation in this scope.";

    [Test]
    public async Task ScalarOrderCannotBeDiscardedByVectorLoweringAndHealthySearchStillWorks()
    {
        using var db = new TestDatabase();
        AdapterVectorWholeFlow.Seed(db);
        var canonical = AdapterVectorWholeFlow.Typed(db);
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var position = db.Store.Position;
        var before = db.Store.GetReadDiagnostics();
        GraphSearchRequest? lowered = null;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => lowered = KeyLoadQuery.From<VectorAttachmentDocument>(
            db.Partition, AdapterVectorWholeFlow.Collection, UnitClientOptions.Translation())
            .OrderBy(row => QueryFunctions.DocumentId(row)).AttachVector(row => row.Embedding,
                canonical.Search.Vector!.Value, AdapterVectorWholeFlow.Space(), canonical.Scope!));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(error.Message).IsEqualTo(UnsupportedDetail);
        await Assert.That(lowered).IsNull();
        await Assert.That(db.Store.GetReadDiagnostics()).IsEqualTo(before);
        await AssertUnchangedAsync(db, image, position);
        await HealthyAsync(db);
        await AssertUnchangedAsync(db, image, position);
    }

    [Test]
    public async Task AttachmentItemBoundRejectsBeforeNativeReadAndHealthySearchStillWorks()
    {
        using var db = new TestDatabase();
        AdapterVectorWholeFlow.Seed(db);
        var canonical = AdapterVectorWholeFlow.Typed(db);
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var position = db.Store.Position;
        var before = db.Store.GetReadDiagnostics();
        GraphSearchRequest? lowered = null;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => lowered = KeyLoadQuery.From<VectorAttachmentDocument>(
            db.Partition, AdapterVectorWholeFlow.Collection,
            UnitClientOptions.Translation(new QueryTranslationOptions { MaximumConstantArrayItems = AttachmentItems }))
            .AttachVector(row => row.Embedding, canonical.Search.Vector!.Value,
                AdapterVectorWholeFlow.Space(), canonical.Scope!));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(error.Message).IsEqualTo(LimitDetail);
        await Assert.That(lowered).IsNull();
        await Assert.That(db.Store.GetReadDiagnostics()).IsEqualTo(before);
        await AssertUnchangedAsync(db, image, position);
        await HealthyAsync(db);
        await AssertUnchangedAsync(db, image, position);
    }

    [Test]
    public async Task PersistedDeniedPrincipalRejectsSqlBuilderAndJsonWithoutEffectsThenHealthyRanksMatch()
    {
        using var db = new TestDatabase();
        AdapterVectorWholeFlow.Seed(db);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Denied, Tenant, [], []))).Get<PrincipalRecord>();
        var query = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var search = new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var token = TestContext.Current!.Execution.CancellationToken;
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var position = db.Store.Position;
        GraphSearchResult? partial = null;
        var sqlError = (await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial = await query.SearchSqlAsync(
            Denied, AdapterVectorWholeFlow.SqlRequest(db), token)))
            ?? throw new InvalidOperationException("The denied SQL operation must yield its exact failure.");
        await Assert.That(sqlError.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(sqlError.Message).IsEqualTo(DeniedDetail);
        foreach (var request in new[] { AdapterVectorWholeFlow.Typed(db), AdapterVectorWholeFlow.CSharp(db),
            AdapterVectorWholeFlow.JsonRoundTrip(AdapterVectorWholeFlow.CSharp(db)) })
        {
            var error = (await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial = await search.GraphSearchAsync(
                Denied, request, token))) ?? throw new InvalidOperationException("The denied typed operation must yield its exact failure.");
            await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(error.Message).IsEqualTo(DeniedDetail);
        }
        await Assert.That(partial).IsNull();
        await AssertUnchangedAsync(db, image, position);
        await AdapterVectorWholeFlow.HealthyAsync(db, query, search, token);
        await AssertUnchangedAsync(db, image, position);
    }

    private static Task HealthyAsync(TestDatabase db) => AdapterVectorWholeFlow.HealthyAsync(db,
        new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution()),
        new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution()), TestContext.Current!.Execution.CancellationToken);

    private static async Task AssertUnchangedAsync(TestDatabase db, string[] image, long position)
    {
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(db.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
