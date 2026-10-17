using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextGenerationInvalidationOverlap
{
    private const string Collection = "native-text-invalidation-overlap";
    private const string Field = "/text";
    private const string Query = "needle";

    internal static async Task RunAsync(CancellationToken cancellation)
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle original\"}"));
        var root = Path.Combine(database.Directory, Collection);
        using var projection = new NativeTextProjection(root, UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());
        var request = new SearchRequest(database.Partition, Collection, Field, Query);
        var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var original = await engine.SearchAsync("root", request, cancellation);
        var retiredBudget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        using var retiredLease = projection.Acquire(CaptureScope(database), retiredBudget);
        retiredLease.BeginRecord(original[0].Document.Reference, original[0].Document.Revision);
        retiredLease.ObserveToken(Query);
        await RebuildAfterInvalidationAsync(database, projection, engine, request, root, original,
            retiredLease, retiredBudget, cancellation);
    }

    private static async Task RebuildAfterInvalidationAsync(TestDatabase database, NativeTextProjection projection,
        SearchEngine engine, SearchRequest request, string root, RankedDocument[] original,
        ITextProjectionLease retiredLease, ReadExecutionBudget retiredBudget, CancellationToken cancellation)
    {
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle revised\"}"));
        var current = await engine.SearchAsync("root", request, cancellation);
        using var invalidated = projection.Acquire(CaptureScope(database), new(UnitExecutionOptions.DatabaseLimits(database.Database.Limits)));
        var mismatch = Assert.ThrowsExactly<KeyLoadException>(() => invalidated.BeginRecord(
            new(database.Partition, Collection, "missing"), current[0].Document.Revision));
        await Assert.That(mismatch.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        invalidated.Dispose();
        var rebuilt = await engine.SearchAsync("root", request, cancellation);
        await Assert.That(rebuilt).HasSingleItem();
        await Assert.That(rebuilt[0].Document.Revision).IsEqualTo(current[0].Document.Revision);
        await Assert.That(GenerationPaths(root).Length).IsEqualTo(2);
        retiredLease.VerifyCandidates([Query], [original[0].Document.Reference], retiredBudget);
        retiredLease.Dispose();
        await Assert.That(GenerationPaths(root)).HasSingleItem();
    }

    private static TextProjectionScope CaptureScope(TestDatabase database)
    {
        var identity = database.Store.Identity;
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal("root")))!;
        var resource = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            database.Partition.TenantId, database.Partition.DatabaseId, Collection)))!;
        return new(identity.NodeId, identity.Incarnation, identity.FormatVersion, identity.ReadGeneration,
            database.Store.Position, database.Partition, Collection, Field, principal.Id, principal.PolicyEpoch,
            resource.SchemaVersion);
    }

    private static string[] GenerationPaths(string root)
        => Directory.EnumerateDirectories(root)
            .Where(path => Path.GetFileName(path).StartsWith(NativeTextProtocol.GenerationPrefix,
                StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
}
