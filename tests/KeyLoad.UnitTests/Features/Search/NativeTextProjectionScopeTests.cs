using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextProjectionScopeTests
{
    private const string Collection = "native-text-scope";
    private const string TextPath = "/text";
    private const string Query = "needle";

    [Test]
    public async Task TrustedInternalSchemaScopeMismatchCannotReuseTheCurrentGeneration()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"),
            new PutDocument(Collection, "two", "{\"text\":\"needle\"}"));
        using var projection = new NativeTextProjection(Path.Combine(database.Directory, "native-text"),
            database.Database.Limits, database.Store.Identity.NodeId);
        var request = new SearchRequest(database.Partition, Collection, TextPath, Query);
        var oracle = await new SearchEngine(database.Database).SearchAsync("root", request);
        var search = new SearchEngine(database.Database, projection);
        var canonical = await search.SearchAsync("root", request, TestContext.Current!.Execution.CancellationToken);
        var canonicalGeneration = CurrentGeneration(database);
        var scope = CaptureScope(database);
        var alteredSchemaScope = scope with { SchemaVersion = checked(scope.SchemaVersion + 1) };
        var documents = ReadCanonicalDocuments(database);
        var budget = new ReadExecutionBudget(database.Database.Limits);
        using (var lease = projection.Acquire(alteredSchemaScope, budget))
        {
            ObserveCanonicalCorpus(lease, documents);
            lease.VerifyCandidates([Query], documents.Select(document => document.Reference).ToArray(), budget);
        }
        var mismatchedGeneration = CurrentGeneration(database);
        var rebuilt = await search.SearchAsync("root", request, TestContext.Current!.Execution.CancellationToken);

        await Assert.That(canonicalGeneration).IsNotEqualTo(mismatchedGeneration);
        await Assert.That(CurrentGeneration(database)).IsNotEqualTo(mismatchedGeneration);
        await Assert.That(canonical.Select(row => row.Document.Reference.Id).ToArray())
            .IsEquivalentTo(oracle.Select(row => row.Document.Reference.Id).ToArray(), CollectionOrdering.Matching);
        await Assert.That(rebuilt.Select(row => row.Document.Reference.Id).ToArray())
            .IsEquivalentTo(oracle.Select(row => row.Document.Reference.Id).ToArray(), CollectionOrdering.Matching);
    }

    private static void ObserveCanonicalCorpus(ITextProjectionLease lease, DocumentRecord[] documents)
    {
        foreach (var document in documents)
        {
            lease.BeginRecord(document.Reference, document.Revision);
            lease.ObserveToken(Query);
        }
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

    private static DocumentRecord[] ReadCanonicalDocuments(TestDatabase database)
        => database.Store.Read(view => view.Scan(DocumentStorageKeys.Prefix(database.Partition, Collection),
                database.Database.Limits.MaxScanRecords).Records
            .Select(record => NativeSerialization.Deserialize<DocumentRecord>(record.Value.Span)).ToArray());

    private static string CurrentGeneration(TestDatabase database)
        => Directory.EnumerateDirectories(Path.Combine(database.Directory, "native-text"))
            .Single(path => Path.GetFileName(path).StartsWith(NativeTextProtocol.GenerationPrefix,
                StringComparison.Ordinal));
}
