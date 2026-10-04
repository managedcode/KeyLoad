using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;

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
        var root = Path.Combine(database.Directory, "native-text-generation-lifetime");
        using var projection = NewProjection(database, root);
        var cancellation = TestContext.Current!.Execution.CancellationToken;
        var request = new SearchRequest(database.Partition, Collection, TextPath, Query);
        var original = await new SearchEngine(database.Database, projection).SearchAsync("root", request, cancellation);
        var originalGeneration = GenerationPaths(root).Single();
        var oldScope = CaptureScope(database);
        var oldBudget = new ReadExecutionBudget(database.Database.Limits);
        using var oldLease = projection.Acquire(oldScope, oldBudget);
        oldLease.BeginRecord(original[0].Document.Reference, original[0].Document.Revision);
        oldLease.ObserveToken(Query);

        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle revised\"}"));
        var replacement = await new SearchEngine(database.Database, projection).SearchAsync("root", request,
            cancellation);
        var generationsWhileBorrowed = GenerationPaths(root);
        using var currentLease = projection.Acquire(CaptureScope(database), new(database.Database.Limits));
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle final\"}"));
        var saturated = Assert.ThrowsExactly<KeyLoadException>(() =>
            new SearchEngine(database.Database, projection).Search("root", request, cancellation));
        await Assert.That(saturated.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        currentLease.Dispose();
        var final = await new SearchEngine(database.Database, projection).SearchAsync("root", request, cancellation);
        oldLease.VerifyCandidates([Query], [original[0].Document.Reference], oldBudget);

        await Assert.That(replacement).HasSingleItem();
        await Assert.That(final).HasSingleItem();
        await Assert.That(replacement[0].Document.Revision).IsGreaterThan(original[0].Document.Revision);
        await Assert.That(generationsWhileBorrowed.Length).IsEqualTo(2);
        await Assert.That(generationsWhileBorrowed).Contains(originalGeneration);
        oldLease.Dispose();
        await Assert.That(GenerationPaths(root).Length).IsEqualTo(1);
        var current = await new SearchEngine(database.Database, projection).SearchAsync("root", request, cancellation);
        await Assert.That(current).HasSingleItem();
        await Assert.That(current[0].Document.Revision).IsEqualTo(final[0].Document.Revision);
    }

    [Test]
    public async Task FailedRetirementKeepsItsOwnerAndAllowsJoinedShutdownRetry()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle original\"}"));
        var root = Path.Combine(database.Directory, "native-text-retirement-failure");
        using var projection = NewProjection(database, root);
        var cancellation = TestContext.Current!.Execution.CancellationToken;
        var request = new SearchRequest(database.Partition, Collection, TextPath, Query);
        var original = await new SearchEngine(database.Database, projection).SearchAsync("root", request, cancellation);
        var retired = GenerationPaths(root).Single();
        var oldScope = CaptureScope(database);
        var oldBudget = new ReadExecutionBudget(database.Database.Limits);
        var oldLease = projection.Acquire(oldScope, oldBudget);
        oldLease.BeginRecord(original[0].Document.Reference, original[0].Document.Revision);
        oldLease.ObserveToken(Query);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle revised\"}"));
        var replacement = await new SearchEngine(database.Database, projection).SearchAsync("root", request,
            cancellation);
        var foreign = Path.Combine(retired, "foreign-owned-by-test.bin");
        await File.WriteAllBytesAsync(foreign, [0x31, 0x52, 0x73], cancellation);
        oldLease.VerifyCandidates([Query], [original[0].Document.Reference], oldBudget);
        var failure = Assert.ThrowsExactly<KeyLoadException>(oldLease.Dispose);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(Directory.Exists(retired)).IsTrue();

        File.Delete(foreign);
        projection.Dispose();
        await Assert.That(Directory.Exists(root)).IsTrue();
        await Assert.That(GenerationPaths(root).Length).IsEqualTo(1);
        using var recovered = NewProjection(database, root);
        var healthy = await new SearchEngine(database.Database, recovered).SearchAsync("root", request, cancellation);
        await Assert.That(healthy).HasSingleItem();
        await Assert.That(healthy[0].Document.Revision).IsEqualTo(replacement[0].Document.Revision);
    }

    [Test]
    public async Task AggregateCensusCanRunWhileASecondNativeGenerationIsPausedBetweenPostings()
        => await NativeTextGenerationCensusOverlap.RunAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task InvalidatedCurrentCanRebuildWhileItsRetiredNativeLeaseRemainsBorrowed()
        => await NativeTextGenerationInvalidationOverlap.RunAsync(TestContext.Current!.Execution.CancellationToken);

    private static NativeTextProjection NewProjection(TestDatabase database, string root)
        => new(root, database.Database.Limits, database.Store.Identity.NodeId);

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
