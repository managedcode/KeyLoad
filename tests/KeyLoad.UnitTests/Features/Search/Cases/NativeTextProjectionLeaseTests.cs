using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextProjectionLeaseTests
{
    private const string Collection = "native-text-lease";
    private const string TextPath = "/text";

    [Test]
    public async Task SaturationAndCancellationReleaseNativeLeaseForHealthySearch()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        using var projection = new NativeTextProjection(Path.Combine(database.Directory, "native-text"), UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());
        var scope = CaptureScope(database);
        using (var held = projection.Acquire(scope, new(UnitExecutionOptions.DatabaseLimits(database.Database.Limits))))
        {
            var saturated = Assert.ThrowsExactly<KeyLoadException>(() =>
                projection.Acquire(scope, new(UnitExecutionOptions.DatabaseLimits(database.Database.Limits))));
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

    [Test]
    public async Task RecordAndTokenBudgetFailuresPreservePositionAndReleaseLease()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"),
            new PutDocument(Collection, "two", "{\"text\":\"needle\"}"));
        using var projection = new NativeTextProjection(Path.Combine(database.Directory, "native-text"), UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());
        var request = new SearchRequest(database.Partition, Collection, TextPath, "needle");
        var position = database.Store.Position;
        var recordBound = BoundedEngine(database, new() { MaxScanRecords = 1 }, projection);
        var recordFailure = Assert.ThrowsExactly<KeyLoadException>(() => recordBound.Search("root", request));
        var tokenBound = BoundedEngine(database, new() { MaxSearchTextTokens = 1 }, projection);
        var tokenFailure = Assert.ThrowsExactly<KeyLoadException>(() => tokenBound.Search("root", request));

        await Assert.That(recordFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(tokenFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var recovered = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root", request,
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(recovered.Length).IsEqualTo(2);
        await Assert.That(recovered.Select(row => row.Document.Reference.Id).Order(StringComparer.Ordinal))
            .IsEquivalentTo(new[] { "one", "two" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static SearchEngine BoundedEngine(TestDatabase database, DatabaseLimits limits, ITextProjection projection)
        => new(new DatabaseEngine(database.Store, database.Database.Authorization, UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance), UnitExecutionOptions.QueryExecution(), projection);

    private static async Task<OperationCanceledException> CancelActiveVerificationAsync(TestDatabase database,
        NativeTextProjection projection)
    {
        using var cancellation = new CancellationTokenSource();
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits), cancellationToken: cancellation.Token);
        using var lease = projection.Acquire(CaptureScope(database), budget);
        var document = database.Store.Read(view => view.Scan(DocumentStorageKeys.Prefix(database.Partition, Collection),
            database.Database.Limits.MaxScanRecords).Records.Select(record =>
            NativeSerialization.Deserialize<DocumentRecord>(record.Value.Span)).Single());
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
