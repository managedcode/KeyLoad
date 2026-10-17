using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextGenerationLifetimeTests
{
    private const string Collection = "native-text-generation-lifetime";
    private const string TextPath = "/text";
    private const string Query = "needle";

    [Test]
    public async Task PublishedReplacementKeepsOldNativeReaderUntilItsLastLeaseSettles()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle original\"}"));
        var cleanup = new NativeTextGenerationFixtureLifetime();
        await cleanup.RunAsync(() => RunPublishedReplacementAsync(database, cleanup));
    }

    private static async Task RunPublishedReplacementAsync(TestDatabase database,
        NativeTextGenerationFixtureLifetime cleanup)
    {
        var root = Path.Combine(database.Directory, "native-text-generation-lifetime");
        var projection = cleanup.TrackProjection(NewProjection(database, root));
        var cancellation = TestContext.Current!.Execution.CancellationToken;
        var request = new SearchRequest(database.Partition, Collection, TextPath, Query);
        var original = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root", request, cancellation);
        var originalGeneration = GenerationPaths(root).Single();
        var oldScope = CaptureScope(database);
        var oldBudget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        var oldLease = cleanup.TrackLease(projection.Acquire(oldScope, oldBudget));
        oldLease.BeginRecord(original[0].Document.Reference, original[0].Document.Revision);
        oldLease.ObserveToken(Query);
        var (replacement, final, generationCount) = await ReplaceWithBorrowedReadersAsync(database, projection, cleanup,
            request, cancellation);
        oldLease.VerifyCandidates([Query], [original[0].Document.Reference], oldBudget);
        await Assert.That(replacement).HasSingleItem();
        await Assert.That(final).HasSingleItem();
        await Assert.That(replacement[0].Document.Revision).IsGreaterThan(original[0].Document.Revision);
        await Assert.That(generationCount).IsEqualTo(2);
        await Assert.That(GenerationPaths(root)).Contains(originalGeneration);
        cleanup.SettleLease(oldLease);
        await Assert.That(GenerationPaths(root).Length).IsEqualTo(1);
        var current = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root", request, cancellation);
        await Assert.That(current).HasSingleItem();
        await Assert.That(current[0].Document.Revision).IsEqualTo(final[0].Document.Revision);
    }

    private static async Task<(RankedDocument[] Replacement, RankedDocument[] Final, int GenerationCount)>
        ReplaceWithBorrowedReadersAsync(TestDatabase database, NativeTextProjection projection,
            NativeTextGenerationFixtureLifetime cleanup, SearchRequest request, CancellationToken cancellation)
    {
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle revised\"}"));
        var replacement = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root", request,
            cancellation);
        var generations = GenerationPaths(Path.Combine(database.Directory, "native-text-generation-lifetime"));
        var currentLease = cleanup.TrackLease(projection.Acquire(CaptureScope(database), new(UnitExecutionOptions.DatabaseLimits(database.Database.Limits))));
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle final\"}"));
        var saturated = Assert.ThrowsExactly<KeyLoadException>(() =>
            new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).Search("root", request, cancellation));
        await Assert.That(saturated.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        cleanup.SettleLease(currentLease);
        var final = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root", request, cancellation);
        return (replacement, final, generations.Length);
    }

    [Test]
    public async Task FailedRetirementKeepsItsOwnerAndAllowsJoinedShutdownRetry()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle original\"}"));
        var cleanup = new NativeTextGenerationFixtureLifetime();
        await cleanup.RunAsync(() => RunFailedRetirementAsync(database, cleanup));
    }

    private static async Task RunFailedRetirementAsync(TestDatabase database,
        NativeTextGenerationFixtureLifetime cleanup)
    {
        var root = Path.Combine(database.Directory, "native-text-retirement-failure");
        var projection = cleanup.TrackProjection(NewProjection(database, root));
        var cancellation = TestContext.Current!.Execution.CancellationToken;
        var request = new SearchRequest(database.Partition, Collection, TextPath, Query);
        var original = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root", request, cancellation);
        var retired = GenerationPaths(root).Single();
        var oldBudget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        var oldLease = cleanup.TrackLease(projection.Acquire(CaptureScope(database), oldBudget));
        oldLease.BeginRecord(original[0].Document.Reference, original[0].Document.Revision);
        oldLease.ObserveToken(Query);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle revised\"}"));
        var replacement = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root", request,
            cancellation);
        await AssertRetirementDeniedAsync(cleanup, oldLease, retired, original[0].Document.Reference,
            oldBudget, cancellation);
        File.Delete(Path.Combine(retired, "foreign-owned-by-test.bin"));
        cleanup.ShutdownProjection(projection);
        await Assert.That(Directory.Exists(root)).IsTrue();
        await Assert.That(GenerationPaths(root).Length).IsEqualTo(1);
        var recovered = cleanup.TrackProjection(NewProjection(database, root));
        var healthy = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), recovered).SearchAsync("root", request, cancellation);
        await Assert.That(healthy).HasSingleItem();
        await Assert.That(healthy[0].Document.Revision).IsEqualTo(replacement[0].Document.Revision);
    }

    private static async Task AssertRetirementDeniedAsync(NativeTextGenerationFixtureLifetime cleanup,
        ITextProjectionLease lease, string retired, EntityRef originalReference,
        ReadExecutionBudget oldBudget, CancellationToken cancellation)
    {
        var foreign = Path.Combine(retired, "foreign-owned-by-test.bin");
        await File.WriteAllBytesAsync(foreign, [0x31, 0x52, 0x73], cancellation);
        lease.VerifyCandidates([Query], [originalReference], oldBudget);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => cleanup.SettleLease(lease));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(Directory.Exists(retired)).IsTrue();
    }

    [Test]
    public async Task LivePreflightRejectsUnknownEntryAndPreservesTheBorrowedGeneration()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle original\"}"));
        var cleanup = new NativeTextGenerationFixtureLifetime();
        await cleanup.RunAsync(() => RunLivePreflightAsync(database, cleanup));
    }

    private static async Task RunLivePreflightAsync(TestDatabase database,
        NativeTextGenerationFixtureLifetime cleanup)
    {
        var root = Path.Combine(database.Directory, "native-text-live-preflight");
        var projection = cleanup.TrackProjection(NewProjection(database, root));
        var cancellation = TestContext.Current!.Execution.CancellationToken;
        var request = new SearchRequest(database.Partition, Collection, TextPath, Query);
        var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var original = await engine.SearchAsync("root", request, cancellation);
        var originalPath = GenerationPaths(root).Single();
        var leaseBudget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        var lease = cleanup.TrackLease(projection.Acquire(CaptureScope(database), leaseBudget));
        lease.BeginRecord(original[0].Document.Reference, original[0].Document.Revision);
        lease.ObserveToken(Query);
        await AssertLivePreflightFailureAsync(database, engine, originalPath, cancellation);
        var replacement = await engine.SearchAsync("root", request, cancellation);
        lease.VerifyCandidates([Query], [original[0].Document.Reference], leaseBudget);
        await Assert.That(replacement).HasSingleItem();
        await Assert.That(replacement[0].Document.Revision).IsGreaterThan(original[0].Document.Revision);
        await Assert.That(GenerationPaths(root).Length).IsEqualTo(2);
        cleanup.SettleLease(lease);
    }

    private static async Task AssertLivePreflightFailureAsync(TestDatabase database, SearchEngine engine,
        string originalPath, CancellationToken cancellation)
    {
        var unknownPath = Path.Combine(originalPath, "unknown-live-preflight.bin");
        var unknownBytes = new byte[] { 0x31, 0x52, 0x73 };
        await File.WriteAllBytesAsync(unknownPath, unknownBytes, cancellation);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle revised\"}"));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Search("root",
            new SearchRequest(database.Partition, Collection, TextPath, Query), cancellation));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(Directory.Exists(originalPath)).IsTrue();
        await Assert.That(GenerationPaths(Path.GetDirectoryName(originalPath)!).Length).IsEqualTo(1);
        await Assert.That((await File.ReadAllBytesAsync(unknownPath, cancellation)).SequenceEqual(unknownBytes)).IsTrue();
        File.Delete(unknownPath);
    }

    [Test]
    public async Task AggregateCensusCanRunWhileASecondNativeGenerationIsPausedBetweenPostings()
        => await NativeTextGenerationCensusOverlap.RunAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task InvalidatedCurrentCanRebuildWhileItsRetiredNativeLeaseRemainsBorrowed()
        => await NativeTextGenerationInvalidationOverlap.RunAsync(TestContext.Current!.Execution.CancellationToken);

    private static NativeTextProjection NewProjection(TestDatabase database, string root)
        => new(root, UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());

    private static string[] GenerationPaths(string root)
        => Directory.EnumerateDirectories(root)
            .Where(path => Path.GetFileName(path).StartsWith(NativeTextProtocol.GenerationPrefix,
                StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();

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
