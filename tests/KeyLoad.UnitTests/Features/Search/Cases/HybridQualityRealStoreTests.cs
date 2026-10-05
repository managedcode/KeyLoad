using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class HybridQualityRealStoreTests
{
    private const string ProjectionFolder = "hybrid-quality-native-text";
    private const string ObservationKind = "controlled-search-quality";

    [Test]
    public async Task AcSearch007FixedQrelsMeasureRealNativeThreeWaySearchAndWindowSensitivity()
    {
        using var database = new TestDatabase();
        HybridQualityCorpus.ValidateDefinitions();
        HybridQualityCorpus.Seed(database);
        using var projection = CreateProjection(database);
        var exactEngine = new SearchEngine(database.Database);
        var nativeEngine = new SearchEngine(database.Database, projection);
        var initialPosition = database.Store.Position;
        var initialAuthority = CaptureAuthority(database);
        string? generation = null;
        var token = TestContext.Current!.Execution.CancellationToken;

        foreach (var query in HybridQualityCorpus.Queries)
        {
            await EvaluateQueryAsync(database, exactEngine, nativeEngine, query, initialPosition,
                initialAuthority, generation is null ? value => generation = value : null, token);
        }

        await Assert.That(database.Store.Position).IsEqualTo(initialPosition);
        await Assert.That(CaptureAuthority(database).AsSpan().SequenceEqual(initialAuthority)).IsTrue();
        await Assert.That(generation).IsNotNull();
        await Assert.That(CurrentGeneration(database)).IsEqualTo(generation);
    }

    private static async Task EvaluateQueryAsync(TestDatabase database, SearchEngine exactEngine,
        SearchEngine nativeEngine, HybridQualityQuery query, long expectedPosition,
        byte[] expectedAuthority, Action<string>? recordGeneration, CancellationToken token)
    {
        var nativeBranches = await HybridQualityBranchCapture.CaptureAsync(nativeEngine, database, query, token);
        var exactBranches = await HybridQualityBranchCapture.CaptureAsync(exactEngine, database, query, token);
        await HybridQualityAssertions.AssertBranchesEquivalentAsync(exactBranches, nativeBranches);
        var request = HybridQualityCorpus.GraphRequest(database, query);
        var exact = await exactEngine.GraphSearchAsync(HybridQualityCorpus.PrincipalId, request, token);
        var native = await nativeEngine.GraphSearchAsync(HybridQualityCorpus.PrincipalId, request, token);
        var repeated = await nativeEngine.GraphSearchAsync(HybridQualityCorpus.PrincipalId, request, token);
        await HybridQualityAssertions.AssertEquivalentAsync(exact.Hits, native.Hits);
        await HybridQualityAssertions.AssertEquivalentAsync(native.Hits, repeated.Hits);
        await HybridQualityAssertions.AssertEligibleAsync(nativeBranches, query);
        await AssertQueryShapeAsync(database, nativeBranches, native.Hits, query);
        var judgments = HybridQualityCorpus.Judgments(query);
        var metrics = HybridQualityMetricOracle.Compute(
            native.Hits.Select(hit => hit.Document.Reference.Id).ToArray(), judgments, query.EligibleIds);
        await HybridQualityAssertions.AssertMetricsInRangeAsync(metrics);
        var windows = HybridQualityWindowHarness.Evaluate(nativeBranches, query, judgments, database.Database.Limits);
        var full = windows.Single(observation => observation.Width == 32);
        await HybridQualityAssertions.AssertProductionMatchesWindow32Async(full, native.Hits);
        await AssertWindowObservationsAsync(windows, nativeBranches, query);
        await Assert.That(database.Store.Position).IsEqualTo(expectedPosition);
        await Assert.That(CaptureAuthority(database).AsSpan().SequenceEqual(expectedAuthority)).IsTrue();
        recordGeneration?.Invoke(CurrentGeneration(database));
        WriteObservation(query, expectedPosition, nativeBranches, native.Hits, metrics, windows);
    }

    private static async Task AssertQueryShapeAsync(TestDatabase database, ImmutableArray<HybridQualityBranch> branches,
        IReadOnlyList<RankedDocument> production, HybridQualityQuery query)
    {
        if (query.Name == "missing-text")
        {
            await Assert.That(branches.Single(branch => branch.Kind == GlobalBranchKind.Text).Hits).IsEmpty();
            var tiedVectorHits = branches.Single(branch => branch.Kind == GlobalBranchKind.Vector).Hits;
            await HybridQualityTieAssertions.VerifyAsync(database, tiedVectorHits);
        }
        if (query.Name == "graph-selective")
        {
            await Assert.That(branches.Length).IsEqualTo(1);
            await Assert.That(production.Select(hit => hit.Document.Reference.Id).Order(StringComparer.Ordinal).ToArray())
                .IsEquivalentTo(query.EligibleIds.Order(StringComparer.Ordinal).ToArray(), CollectionOrdering.Matching);
        }
        if (query.Name == "empty-allowlist")
        {
            await Assert.That(production).IsEmpty();
        }
    }

    private static async Task AssertWindowObservationsAsync(
        ImmutableArray<HybridQualityWindowObservation> windows, ImmutableArray<HybridQualityBranch> branches,
        HybridQualityQuery query)
    {
        await Assert.That(windows.Select(window => window.Width).ToArray())
            .IsEquivalentTo([1, 4, 8, 32], CollectionOrdering.Matching);
        foreach (var window in windows)
        {
            var shouldTruncate = branches.Any(branch => branch.Hits.Length > window.Width);
            await Assert.That(window.AnyTruncated).IsEqualTo(shouldTruncate);
            await HybridQualityAssertions.AssertMetricsInRangeAsync(window.Metrics);
        }
        var full = windows.Single(window => window.Width == 32);
        var expectedUnion = branches.SelectMany(branch => branch.Hits)
            .Select(hit => hit.Document.Reference.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(full.CandidateIds).IsEquivalentTo(expectedUnion, CollectionOrdering.Matching);
        var expectedRecall = HybridQualityMetricOracle.CandidateRecall(expectedUnion,
            HybridQualityCorpus.Judgments(query), query.EligibleIds);
        await Assert.That(full.CandidateRecall).IsEqualTo(expectedRecall);
    }

    private static NativeTextProjection CreateProjection(TestDatabase database)
        => new(Path.Combine(database.Directory, ProjectionFolder), database.Database.Limits,
            database.Store.Identity.NodeId);

    private static byte[] CaptureAuthority(TestDatabase database)
    {
        var records = database.Store.Read(view => new[]
        {
            NativeSerialization.Serialize(view.GetRecord<PrincipalRecord>(
                KeySpace.Principal(HybridQualityCorpus.PrincipalId))!),
            NativeSerialization.Serialize(view.GetRecord<ResourceDefinition>(KeySpace.Resource(
                database.Partition.TenantId, database.Partition.DatabaseId, HybridQualityCorpus.Collection))!),
            NativeSerialization.Serialize(view.GetRecord<ResourceDefinition>(KeySpace.Resource(
                database.Partition.TenantId, database.Partition.DatabaseId, HybridQualityCorpus.Graph))!)
        });
        var total = records.Sum(record => sizeof(int) + record.Length);
        var output = new byte[total];
        var offset = 0;
        foreach (var record in records)
        {
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset, sizeof(int)), record.Length);
            offset += sizeof(int);
            record.CopyTo(output, offset);
            offset += record.Length;
        }
        return output;
    }

    private static string CurrentGeneration(TestDatabase database)
        => Directory.EnumerateDirectories(Path.Combine(database.Directory, ProjectionFolder))
            .Select(Path.GetFileName)
            .Single(name => name!.StartsWith(NativeTextProtocol.GenerationPrefix, StringComparison.Ordinal))!;

    private static void WriteObservation(HybridQualityQuery query, long position,
        ImmutableArray<HybridQualityBranch> branches, IReadOnlyList<RankedDocument> production,
        HybridQualityMetrics metrics, ImmutableArray<HybridQualityWindowObservation> windows)
    {
        var output = new
        {
            kind = ObservationKind,
            query = query.Name,
            committedPosition = position,
            eligibleCount = query.EligibleIds.Length,
            gradeCount = query.Grades.Length,
            branchIds = branches.ToDictionary(branch => branch.Name,
                branch => branch.Hits.Select(hit => hit.Document.Reference.Id).ToArray(), StringComparer.Ordinal),
            branchScores = branches.ToDictionary(branch => branch.Name,
                branch => branch.Hits.Select(hit => hit.Score).ToArray(), StringComparer.Ordinal),
            productionIds = production.Select(hit => hit.Document.Reference.Id).ToArray(),
            metrics,
            windows = windows.Select(window => new
            {
                window.Width,
                candidateCount = window.CandidateIds.Length,
                window.CandidateRecall,
                window.AnyTruncated,
                window.FusedIds,
                window.Metrics
            }).ToArray()
        };
        Console.WriteLine(JsonSerializer.Serialize(output));
    }
}
