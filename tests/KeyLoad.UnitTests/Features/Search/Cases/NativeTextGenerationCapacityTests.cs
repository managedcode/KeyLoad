using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextGenerationCapacityTests
{
    private const string Collection = "native-text-generation-capacity";
    private const string Field = "/text";

    [Test]
    public async Task ThreeOwnedLeavesAreAcceptedAndFourthIsRejectedWithoutDeletingThem()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var root = InitializeRoot(database);
        var scope = CaptureScope(database);
        var leaves = CreateOwnerLeaves(root, database, scope, 3);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativeTextFiles.WriteOwner(root,
            NativeTextValidation.GenerationLeaf(), database.Store.Identity.NodeId, scope, database.Database.Limits));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(GenerationPaths(root)).IsEquivalentTo(leaves, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RestartPreflightsAllThreeLeavesBeforeAnyRetirement()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle original\"}"));
        var root = Path.Combine(database.Directory, "native-text-generation-capacity");
        using var projection = new NativeTextProjection(root, database.Database.Limits,
            database.Store.Identity.NodeId);
        var cancellation = TestContext.Current!.Execution.CancellationToken;
        var request = new SearchRequest(database.Partition, Collection, Field, "needle");
        var original = await new SearchEngine(database.Database, projection).SearchAsync("root", request, cancellation);
        var retired = Directory.EnumerateDirectories(root).Single();
        var oldScope = CaptureScope(database);
        var oldBudget = new ReadExecutionBudget(database.Database.Limits);
        using var oldLease = projection.Acquire(oldScope, oldBudget);
        oldLease.BeginRecord(original[0].Document.Reference, original[0].Document.Revision);
        oldLease.ObserveToken("needle");
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle revised\"}"));
        _ = await new SearchEngine(database.Database, projection).SearchAsync("root", request, cancellation);
        oldLease.VerifyCandidates(["needle"], [original[0].Document.Reference], oldBudget);
        var scope = CaptureScope(database);
        NativeTextFiles.WriteOwner(root, NativeTextValidation.GenerationLeaf(), database.Store.Identity.NodeId,
            scope, database.Database.Limits);
        await Assert.That(GenerationPaths(root).Length).IsEqualTo(3);
        var foreign = Path.Combine(retired, "foreign-entry.bin");
        await File.WriteAllBytesAsync(foreign, [0x41, 0x62, 0x73],
            cancellation);
        var settlement = Assert.ThrowsExactly<KeyLoadException>(oldLease.Dispose);
        await Assert.That(settlement.Code).IsEqualTo(ErrorCode.FormatUnsupported);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => OpenForRestart(root, database));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(Directory.Exists(retired)).IsTrue();
        await Assert.That(GenerationPaths(root).Length).IsEqualTo(3);
        File.Delete(foreign);
        using var restarted = OpenForRestart(root, database);
        await Assert.That(GenerationPaths(root)).IsEmpty();
        projection.Dispose();
        var healthy = await new SearchEngine(database.Database, restarted).SearchAsync("root", request, cancellation);
        await Assert.That(healthy).HasSingleItem();
    }

    private static string InitializeRoot(TestDatabase database)
        => NativeTextFiles.InitializeRoot(Path.Combine(database.Directory, "native-text-generation-capacity"),
            database.Store.Identity.NodeId, database.Database.Limits);

    private static string[] CreateOwnerLeaves(string root, TestDatabase database, TextProjectionScope scope, int count)
    {
        var leaves = new string[count];
        for (var index = 0; index < count; index++)
        {
            leaves[index] = NativeTextValidation.GenerationLeaf();
            NativeTextFiles.WriteOwner(root, leaves[index], database.Store.Identity.NodeId, scope,
                database.Database.Limits);
        }
        return leaves;
    }

    private static string[] GenerationPaths(string root)
        => Directory.EnumerateDirectories(root)
            .Where(path => Path.GetFileName(path).StartsWith(NativeTextProtocol.GenerationPrefix,
                StringComparison.Ordinal))
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();

    private static NativeTextProjection OpenForRestart(string root, TestDatabase database)
        => new(root, database.Database.Limits, database.Store.Identity.NodeId);

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
}
