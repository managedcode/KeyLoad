using KeyLoad.Core.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Query.Search;
using KeyLoad.Server.Search;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Search;

internal sealed class TextProjectionLeaseTests
{
    private const string Collection = "execution-text-lease";
    private const string TextPath = "/text";

    [Test]
    public async Task SaturationAndCancellationReleaseLeaseForHealthySearch()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        using var projection = new RuntimeTextProjection(Path.Combine(database.Directory, "execution-text"), database.Store.Identity.NodeId, UnitTextOptions.Execution());
        var scope = CaptureScope(database);
        using (var held = projection.Acquire(scope, new()))
        {
            var saturated = Assert.ThrowsExactly<KeyLoadException>(() =>
                projection.Acquire(scope, new()));
            await Assert.That(saturated.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        }

        _ = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root",
            new(database.Partition, Collection, TextPath, "needle"), TestContext.Current!.Execution.CancellationToken);
        var cancelled = await CancelActiveVerificationAsync(database, projection);
        await Assert.That(cancelled.CancellationToken.IsCancellationRequested).IsTrue();
        var result = await Assert.That(await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root",
            new(database.Partition, Collection, TextPath, "needle"),
            TestContext.Current!.Execution.CancellationToken)).HasSingleItem();
        await Assert.That(result.Document.Reference.Id).IsEqualTo("one");
    }

    private static async Task<OperationCanceledException> CancelActiveVerificationAsync(TestDatabase database,
        RuntimeTextProjection projection)
    {
        using var cancellation = new CancellationTokenSource();
        var budget = new ReadExecutionBudget(cancellationToken: cancellation.Token);
        using var lease = projection.Acquire(CaptureScope(database), budget);
        var document = database.Store.Read(view => view.Scan(DocumentStorageKeys.Prefix(database.Partition, Collection), view.MaximumRangeRecords).Records.Select(record =>
            OrleansSerialization.Deserialize<DocumentRecord>(record.Value.Span)).Single());
        lease.BeginRecord(document.Reference, document.Revision);
        lease.ObserveToken("needle");
        await cancellation.CancelAsync();
        return Assert.ThrowsExactly<OperationCanceledException>(() =>
            lease.VerifyCandidates(["needle"], [document.Reference], budget));
    }

    private static TextProjectionScope CaptureScope(TestDatabase database)
    {
        var identity = database.Store.Identity;
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal("root")))!;
        var resource = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            database.Partition.TenantId, database.Partition.DatabaseId, Collection)))!;
        return new(identity.NodeId, identity.Incarnation, identity.FormatVersion, identity.ReadGeneration,
            database.Store.Position, database.Partition, Collection, TextPath, principal.Id, principal.PolicyEpoch,
            resource.SchemaVersion);
    }
}
