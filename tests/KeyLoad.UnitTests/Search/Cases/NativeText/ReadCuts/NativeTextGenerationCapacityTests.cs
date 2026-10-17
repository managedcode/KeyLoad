using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextGenerationCapacityTests
{
    private const string Collection = "native-text-generation-capacity";
    private const string Field = "/text";
    private static readonly byte[] ForeignBytes = [0x41, 0x62, 0x73];

    [Test]
    public async Task ThreeOwnedLeavesAreAcceptedAndFourthIsRejectedWithoutDeletingThem()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var root = InitializeRoot(database);
        var scope = CaptureScope(database);
        var leaves = CreateOwnerLeaves(root, database, scope, 3).Order(StringComparer.Ordinal).ToArray();

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativeTextFiles.WriteOwner(root,
            NativeTextValidation.GenerationLeaf(), database.Store.Identity.NodeId, scope, database.Database.Limits, UnitNativeTextOptions.Execution()));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(GenerationPaths(root).SequenceEqual(leaves)).IsTrue();
    }

    [Test]
    public async Task RestartPreflightsAllThreeLeavesBeforeAnyRetirement()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle original\"}"));
        var root = Path.Combine(database.Directory, "native-text-generation-capacity");
        var cleanup = new NativeTextGenerationFixtureLifetime();
        await cleanup.RunAsync(() => RunRestartScenarioAsync(database, cleanup, root));
    }

    private static async Task RunRestartScenarioAsync(TestDatabase database,
        NativeTextGenerationFixtureLifetime cleanup, string root)
    {
        var projection = cleanup.TrackProjection(OpenForRestart(root, database));
        var cancellation = TestContext.Current!.Execution.CancellationToken;
        var request = new SearchRequest(database.Partition, Collection, Field, "needle");
        var original = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root", request, cancellation);
        var retired = Path.Combine(root, GenerationPaths(root).Single());
        var oldScope = CaptureScope(database);
        var oldBudget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        var oldLease = cleanup.TrackLease(projection.Acquire(oldScope, oldBudget));
        oldLease.BeginRecord(original[0].Document.Reference, original[0].Document.Revision);
        oldLease.ObserveToken("needle");
        await ReplaceDocumentAsync(database, projection, request, cancellation);
        oldLease.VerifyCandidates(["needle"], [original[0].Document.Reference], oldBudget);
        await AssertForeignRetirementAndShutdownAsync(cleanup, projection, oldLease, root, retired);
        var restarted = await AssertThirdLeafRestartAsync(database, cleanup, root, retired, cancellation);
        cleanup.ShutdownProjection(projection);
        var healthy = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), restarted).SearchAsync("root", request, cancellation);
        await Assert.That(healthy).HasSingleItem();
        await Assert.That(healthy[0].Document.Revision).IsGreaterThan(original[0].Document.Revision);
    }

    private static async Task ReplaceDocumentAsync(TestDatabase database, NativeTextProjection projection,
        SearchRequest request, CancellationToken cancellation)
    {
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle revised\"}"));
        var replacement = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root", request, cancellation);
        await Assert.That(replacement).HasSingleItem();
    }

    private static async Task AssertForeignRetirementAndShutdownAsync(NativeTextGenerationFixtureLifetime cleanup,
        NativeTextProjection projection, ITextProjectionLease oldLease, string root, string retired)
    {
        var existingLeaves = GenerationPaths(root);
        await Assert.That(existingLeaves.Length).IsEqualTo(2);
        var foreign = Path.Combine(retired, "foreign-entry.bin");
        await File.WriteAllBytesAsync(foreign, ForeignBytes,
            TestContext.Current!.Execution.CancellationToken);
        var leaseFailure = Assert.ThrowsExactly<KeyLoadException>(() => cleanup.SettleLease(oldLease));
        await Assert.That(leaseFailure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        var shutdownFailure = Assert.ThrowsExactly<KeyLoadException>(() => cleanup.ShutdownProjection(projection));
        await Assert.That(shutdownFailure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(GenerationPaths(root).SequenceEqual(existingLeaves)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(foreign,
            TestContext.Current!.Execution.CancellationToken)).SequenceEqual(ForeignBytes)).IsTrue();
    }

    private static async Task<NativeTextProjection> AssertThirdLeafRestartAsync(TestDatabase database,
        NativeTextGenerationFixtureLifetime cleanup, string root, string retired, CancellationToken cancellation)
    {
        var foreign = Path.Combine(retired, "foreign-entry.bin");
        File.Delete(foreign);
        var existingLeaves = GenerationPaths(root);
        var thirdLeaf = NativeTextValidation.GenerationLeaf();
        NativeTextFiles.WriteOwner(root, thirdLeaf, database.Store.Identity.NodeId, CaptureScope(database),
            database.Database.Limits, UnitNativeTextOptions.Execution());
        var expectedLeaves = existingLeaves.Append(thirdLeaf).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(expectedLeaves.Length).IsEqualTo(3);
        await Assert.That(GenerationPaths(root).SequenceEqual(expectedLeaves)).IsTrue();
        await File.WriteAllBytesAsync(foreign, ForeignBytes, cancellation);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => OpenForRestart(root, database));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(GenerationPaths(root).SequenceEqual(expectedLeaves)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(foreign, cancellation)).SequenceEqual(ForeignBytes)).IsTrue();
        File.Delete(foreign);
        var restarted = cleanup.TrackProjection(OpenForRestart(root, database));
        await Assert.That(GenerationPaths(root)).IsEmpty();
        return restarted;
    }

    private static string InitializeRoot(TestDatabase database)
        => NativeTextFiles.InitializeRoot(Path.Combine(database.Directory, "native-text-generation-capacity"),
            database.Store.Identity.NodeId, database.Database.Limits, UnitNativeTextOptions.Execution());

    private static string[] CreateOwnerLeaves(string root, TestDatabase database, TextProjectionScope scope, int count)
    {
        var leaves = new string[count];
        for (var index = 0; index < count; index++)
        {
            leaves[index] = NativeTextValidation.GenerationLeaf();
            NativeTextFiles.WriteOwner(root, leaves[index], database.Store.Identity.NodeId, scope,
                database.Database.Limits, UnitNativeTextOptions.Execution());
        }
        return leaves;
    }

    private static string[] GenerationPaths(string root)
        => Directory.EnumerateDirectories(root)
            .Where(path => Path.GetFileName(path).StartsWith(NativeTextProtocol.GenerationPrefix,
                StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray();

    private static NativeTextProjection OpenForRestart(string root, TestDatabase database)
        => new(root, UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());

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
