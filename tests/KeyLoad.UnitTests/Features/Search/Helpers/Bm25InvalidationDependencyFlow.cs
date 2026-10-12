using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25InvalidationDependencyFlow
{
    private const string Root = "root";
    private const string Collection = "orders";
    private const string Mutated = "mutation";
    private const string Healthy = "healthy";
    private const string Reader = "bm25-dependency-reader";
    private const string Text = "/text";
    private const string Term = "needle";
    private const string Json = "{\"text\":\"needle\"}";
    private const string ReadGrant = "corpus.read";
    private const string UseGrant = "corpus.use";
    private const string WriteGrant = "corpus.write";

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        try
        {
            using var database = new TestDatabase(nativeReplicaAdmission: true);
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(database), failures);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase database)
    {
        database.Configure(Collection, ResourceKind.Collection);
        await MutationAsync(database, new PutDocument(Collection, Mutated, Json), 1);
        await MutationAsync(database, new PatchDocument(Collection, Mutated,
            [new(Text, PatchKind.Set, "\"needle changed\"")], 1), 2);
        await MutationAsync(database, new PutDocument(Collection, Mutated, Json, 2,
            new RowAccess(OwnerId: Root), ExplicitReplacement: true), 3);
        var access = database.Store.Read(view => view.GetRecord<DocumentRecord>(
            KeyLoad.Core.Features.DocumentStorage.DocumentStorageKeys.RecordKey(database.Partition, Collection, Mutated)))!;
        await Assert.That(access.Access.OwnerId).IsEqualTo(Root);
        await MutationAsync(database, new DeleteDocument(Collection, Mutated, 3), 4);
        await Assert.That(database.Database.GetDocument(Root,
            new(database.Partition, Collection, Mutated))).IsNull();
        await MutationAsync(database, new PutDocument(Collection, Healthy, Json), 5);
        await PolicyAsync(database);
    }

    private static async Task MutationAsync(TestDatabase database, Mutation mutation, long expectedTail)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, database.Partition, [mutation]);
        var original = database.Submit(OperationKind.Batch, request, id: id).Get<CommitReceipt>();
        var entry = database.ReadRetainedEntry(id);
        var stored = OutcomeStoreOracle.ReadPartition(database.Store, database.Partition, Root, id)!.Get<CommitReceipt>();
        var beforeReplay = Image(database);
        var journal = database.ReadJournalCut();
        var replay = database.Submit(OperationKind.Batch, request, id: id).Get<CommitReceipt>();
        await Assert.That(entry.Operation!.Id).IsEqualTo(id);
        await Assert.That(original.CommandId).IsEqualTo(id);
        await Assert.That(JsonDefaults.Serialize(stored))
            .IsEquivalentTo(JsonDefaults.Serialize(original), CollectionOrdering.Matching);
        await SameImageAsync(beforeReplay, Image(database));
        await Assert.That(database.ReadJournalCut()).IsEqualTo(journal);
        await Assert.That(JsonDefaults.Serialize(replay))
            .IsEquivalentTo(JsonDefaults.Serialize(original), CollectionOrdering.Matching);
        await Assert.That(database.Database.GetOutboxStatus(Root, database.Partition).Head.Tail)
            .IsEqualTo(expectedTail);
    }

    private static async Task PolicyAsync(TestDatabase database)
    {
        var principal = new PrincipalRecord(Reader, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)], []);
        ConfigurePrincipal(database, principal);
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        await HealthyAsync(database, search);
        var tail = database.Database.GetOutboxStatus(Root, database.Partition).Head.Tail;
        var denied = principal with
        {
            PolicyEpoch = 2,
            Grants = [new(database.Partition.DatabaseId, Collection, Capability.DocumentsRead)]
        };
        await Assert.That(ConfigurePrincipal(database, denied).PolicyEpoch).IsEqualTo(2L);
        await Assert.That(database.Database.GetOutboxStatus(Root, database.Partition).Head.Tail).IsEqualTo(tail);
        await DeniedAsync(database, search);
        var repaired = principal with { PolicyEpoch = 3 };
        await Assert.That(ConfigurePrincipal(database, repaired).PolicyEpoch).IsEqualTo(3L);
        await HealthyAsync(database, search);
        await ResourcePolicyAsync(database, search, repaired, tail);
    }

    private static async Task ResourcePolicyAsync(TestDatabase database, SearchEngine search,
        PrincipalRecord principal, long tail)
    {
        var current = database.Store.Read(view => view.GetRecord<ResourceDefinition>(
            KeySpace.Resource(database.Partition.TenantId, database.Partition.DatabaseId, Collection)))!;
        var replacement = current with
        {
            SchemaVersion = current.SchemaVersion + 1,
            FieldPolicies = [new(Text, "corpus-private", ReadGrant, UseGrant, WriteGrant)]
        };
        var definition = database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, replacement)
            { ExpectedSchemaVersion = current.SchemaVersion }).Get<ResourceDefinition>();
        await Assert.That(definition.SchemaVersion).IsEqualTo(current.SchemaVersion + 1);
        await Assert.That(database.Database.GetOutboxStatus(Root, database.Partition).Head.Tail).IsEqualTo(tail);
        await DeniedAsync(database, search);
        var repaired = principal with { PolicyEpoch = 4, FieldGrants = [ReadGrant, UseGrant] };
        await Assert.That(ConfigurePrincipal(database, repaired).PolicyEpoch).IsEqualTo(4L);
        await HealthyAsync(database, search);
        await Assert.That(database.Database.GetOutboxStatus(Root, database.Partition).Head.Tail).IsEqualTo(tail);
    }

    private static PrincipalRecord ConfigurePrincipal(TestDatabase database, PrincipalRecord principal)
        => database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal))
            .Get<PrincipalRecord>();

    private static async Task DeniedAsync(TestDatabase database, SearchEngine search)
    {
        var before = Image(database);
        var journal = database.ReadJournalCut();
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            search.SearchAsync(Reader, new(database.Partition, Collection, Text, Term),
                TestContext.Current!.Execution.CancellationToken));
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await SameImageAsync(before, Image(database));
        await Assert.That(database.ReadJournalCut()).IsEqualTo(journal);
    }

    private static async Task SameImageAsync(
        (Guid NodeId, Guid Incarnation, long Position, string[] Rows) before,
        (Guid NodeId, Guid Incarnation, long Position, string[] Rows) after)
    {
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(after.Position).IsEqualTo(before.Position);
        await Assert.That(after.Rows).IsEquivalentTo(before.Rows, CollectionOrdering.Matching);
    }

    private static async Task HealthyAsync(TestDatabase database, SearchEngine search)
    {
        var rows = await search.SearchAsync(Reader, new(database.Partition, Collection, Text, Term),
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(rows).HasSingleItem();
        await Assert.That(rows[0].Document.Reference.Id).IsEqualTo(Healthy);
        await Assert.That(rows[0].Document.Json).IsEqualTo(Json);
    }

    private static (Guid NodeId, Guid Incarnation, long Position, string[] Rows) Image(TestDatabase database)
    {
        database.JoinNativeReadBarrier(TestContext.Current!.Execution.CancellationToken);
        var budget = database.CreateReadWork(TestContext.Current.Execution.CancellationToken);
        return database.Store.Read(view =>
        {
            var page = budget.Scan(view, [], UnitExecutionOptions.DatabaseLimits().Value.MaxScanRecords);
            if (page.HasMore)
            { throw new InvalidOperationException("The full native dependency image exceeded its original scan budget."); }
            var rows = page.Records.Select(row =>
                Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
            return (database.Store.Identity.NodeId, database.Store.Identity.Incarnation,
                database.Store.Position, rows);
        });
    }
}
