using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using KeyLoad.Comparisons;
using ComparisonOperationResult = KeyLoad.Comparisons.OperationResult;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ImmutableComparisonContractTests
{
    private const string SchemaVersionProperty = "schemaVersion";
    private const string TargetsProperty = "targets";
    private const string CasesProperty = "cases";
    private const string RequiredArraysJson = "{\"schemaVersion\":3,\"runId\":\"be464719-904b-4ca8-8c14-fb48886f116b\",\"startedAt\":\"2026-10-02T00:00:00+00:00\",\"options\":{\"topology\":\"Single\",\"seed\":1729,\"documents\":1000,\"operations\":1000,\"warmup\":50,\"repetitions\":3,\"concurrency\":8,\"payloadBytes\":1024,\"dimensions\":32,\"topK\":10,\"timeoutSeconds\":30,\"graphVertices\":256,\"graphFanOut\":3,\"graphDepth\":3},\"datasetSha256\":\"sha\",\"loadModel\":\"closed\",\"hostOs\":\"linux\",\"architecture\":\"x64\",\"logicalProcessors\":4,\"runtime\":\".NET 10\",\"storage\":\"disk\",\"sourceRevision\":null,\"targets\":[],\"cases\":[],\"provenance\":null,\"loadGeneratorImage\":null}";
    private const string FormattedRequiredArraysJson = """
        {
          "schemaVersion": 3,
          "runId": "be464719-904b-4ca8-8c14-fb48886f116b",
          "startedAt": "2026-10-02T00:00:00+00:00",
          "options": {
            "topology": "Single",
            "seed": 1729,
            "documents": 1000,
            "operations": 1000,
            "warmup": 50,
            "repetitions": 3,
            "concurrency": 8,
            "payloadBytes": 1024,
            "dimensions": 32,
            "topK": 10,
            "timeoutSeconds": 30,
            "graphVertices": 256,
            "graphFanOut": 3,
            "graphDepth": 3
          },
          "datasetSha256": "sha",
          "loadModel": "closed",
          "hostOs": "linux",
          "architecture": "x64",
          "logicalProcessors": 4,
          "runtime": ".NET 10",
          "storage": "disk",
          "sourceRevision": null,
          "targets": [],
          "cases": [],
          "provenance": null,
          "loadGeneratorImage": null
        }
        """;

    [Test]
    public async Task AcBct001HarnessCollectionContractsExposeImmutableArraysWithoutPublicArrayAliases()
    {
        var expected = new Dictionary<(Type Type, string Property), Type>
        {
            [(typeof(BenchmarkDocument), nameof(BenchmarkDocument.Vector))] = typeof(ImmutableArray<float>),
            [(typeof(ComparisonOperationResult), nameof(ComparisonOperationResult.Neighbors))] = typeof(ImmutableArray<FoundDocument>?),
            [(typeof(ComparisonOperationResult), nameof(ComparisonOperationResult.Vertices))] = typeof(ImmutableArray<string>?),
            [(typeof(ClusterEvidence), nameof(ClusterEvidence.Observations))] = typeof(ImmutableArray<string>),
            [(typeof(ComparisonCase), nameof(ComparisonCase.Samples))] = typeof(ImmutableArray<OperationSample>),
            [(typeof(ComparisonReport), nameof(ComparisonReport.Targets))] = typeof(ImmutableArray<TargetProfile>),
            [(typeof(ComparisonReport), nameof(ComparisonReport.Cases))] = typeof(ImmutableArray<ComparisonCase>),
            [(typeof(BenchmarkDataset), nameof(BenchmarkDataset.Documents))] = typeof(ImmutableArray<BenchmarkDocument>),
            [(typeof(BenchmarkDataset), nameof(BenchmarkDataset.Edges))] = typeof(ImmutableArray<BenchmarkEdge>)
        };

        foreach (var (key, propertyType) in expected)
        {
            var property = key.Type.GetProperty(key.Property, BindingFlags.Public | BindingFlags.Instance);
            await Assert.That(property).IsNotNull();
            await Assert.That(property!.PropertyType).IsEqualTo(propertyType);
        }

        var mutableAliases = expected.Keys.Select(key => key.Type.GetProperty(key.Property)!)
            .Where(property => property.PropertyType.IsArray)
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}").ToArray();
        await Assert.That(mutableAliases).IsEmpty();
    }

    [Test]
    public async Task AcBct002IndependentSchema3JsonKeepsTopologyAndRequiredArraysOnTheWire()
    {
        var expected = new ComparisonReport(3, Guid.Parse("be464719-904b-4ca8-8c14-fb48886f116b"),
            new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), new ComparisonOptions(), "sha", "closed",
            "linux", "x64", 4, ".NET 10", "disk", null, ImmutableArray<TargetProfile>.Empty,
            ImmutableArray<ComparisonCase>.Empty);

        var actual = JsonSerializer.Serialize(expected, ReportWriter.JsonOptions);
        await Assert.That(actual).IsEqualTo(FormattedRequiredArraysJson);
        using var document = JsonDocument.Parse(actual);
        await Assert.That(document.RootElement.GetProperty(SchemaVersionProperty).GetInt32()).IsEqualTo(3);
        await Assert.That(document.RootElement.GetProperty(TargetsProperty).GetArrayLength()).IsEqualTo(0);
        await Assert.That(document.RootElement.GetProperty(CasesProperty).GetArrayLength()).IsEqualTo(0);
        await Assert.That(actual).Contains("\"topology\": \"Single\"");
        await Assert.That(actual).Contains("\"targets\": []");
        await Assert.That(actual).Contains("\"cases\": []");
    }

    [Test]
    public void AcBct002RequiredReportArraysRejectMissingNullAndDefaultValues()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ComparisonReport>(
            RewriteRequiredArrays(RequiredArraysJson, omit: "cases"), ReportWriter.JsonOptions));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ComparisonReport>(
            RewriteRequiredArrays(RequiredArraysJson, nullValue: "targets"), ReportWriter.JsonOptions));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ComparisonReport>(
            RewriteRequiredArrays(RequiredArraysJson, omit: "targets"), ReportWriter.JsonOptions));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ComparisonReport>(
            RewriteRequiredArrays(RequiredArraysJson, nullValue: "cases"), ReportWriter.JsonOptions));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Serialize(ReportWithDefaultTargets(), ReportWriter.JsonOptions));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Serialize(ReportWithDefaultCases(), ReportWriter.JsonOptions));
    }

    [Test]
    public void AcBct002RequiredCorpusAndEvidenceArraysRejectMissingNullAndDefaultValues()
    {
        const string documentJson = "{\"number\":0,\"id\":\"d000000000\",\"json\":\"{}\",\"vector\":[]}";
        const string evidenceJson = "{\"nodes\":1,\"dataCopies\":1,\"state\":\"ready\",\"observations\":[]}";
        const string caseJson = "{\"target\":\"node\",\"scenario\":\"PointRead\",\"repetition\":0,\"status\":\"ok\",\"detail\":null,\"measurement\":null,\"samples\":[]}";

        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<BenchmarkDocument>(
            documentJson.Replace(",\"vector\":[]", string.Empty, StringComparison.Ordinal), ReportWriter.JsonOptions));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<BenchmarkDocument>(
            documentJson.Replace("\"vector\":[]", "\"vector\":null", StringComparison.Ordinal), ReportWriter.JsonOptions));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Serialize(
            new BenchmarkDocument(0, "d000000000", "{}", default), ReportWriter.JsonOptions));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ClusterEvidence>(
            evidenceJson.Replace(",\"observations\":[]", string.Empty, StringComparison.Ordinal), ReportWriter.JsonOptions));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ClusterEvidence>(
            evidenceJson.Replace("\"observations\":[]", "\"observations\":null", StringComparison.Ordinal), ReportWriter.JsonOptions));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Serialize(
            new ClusterEvidence(1, 1, "ready", default), ReportWriter.JsonOptions));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ComparisonCase>(
            caseJson.Replace(",\"samples\":[]", string.Empty, StringComparison.Ordinal), ReportWriter.JsonOptions));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ComparisonCase>(
            caseJson.Replace("\"samples\":[]", "\"samples\":null", StringComparison.Ordinal), ReportWriter.JsonOptions));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Serialize(
            new ComparisonCase("node", Scenario.PointRead, 0, "ok", null, null, default), ReportWriter.JsonOptions));
    }

    [Test]
    public async Task AcBct002OptionalArraysKeepAbsentDistinctFromPresentEmpty()
    {
        const string absent = "{}";
        const string presentEmpty = "{\"neighbors\":[],\"vertices\":[]}";
        var noOptionalResults = JsonSerializer.Deserialize<ComparisonOperationResult>(absent, ReportWriter.JsonOptions)!;
        var emptyOptionalResults = JsonSerializer.Deserialize<ComparisonOperationResult>(presentEmpty, ReportWriter.JsonOptions)!;

        await Assert.That(noOptionalResults.Neighbors).IsNull();
        await Assert.That(noOptionalResults.Vertices).IsNull();
        await Assert.That(emptyOptionalResults.Neighbors).IsNotNull();
        await Assert.That(emptyOptionalResults.Neighbors!.Value.IsEmpty).IsTrue();
        await Assert.That(emptyOptionalResults.Vertices).IsNotNull();
        await Assert.That(emptyOptionalResults.Vertices!.Value.IsEmpty).IsTrue();
        var explicitNull = JsonSerializer.Deserialize<ComparisonOperationResult>("{\"neighbors\":null,\"vertices\":[]}", ReportWriter.JsonOptions)!;
        await Assert.That(explicitNull.Neighbors).IsNull();
        await Assert.That(JsonSerializer.Deserialize<ComparisonOperationResult>("{\"neighbors\":[],\"vertices\":null}", ReportWriter.JsonOptions)!.Vertices).IsNull();
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Serialize(
            new ComparisonOperationResult(Neighbors: default(ImmutableArray<FoundDocument>)), ReportWriter.JsonOptions));
        await Assert.That(JsonSerializer.Serialize(emptyOptionalResults, ReportWriter.JsonOptions))
            .IsEqualTo("""
                {
                  "document": null,
                  "neighbors": [],
                  "message": null,
                  "queue": null,
                  "vertices": [],
                  "event": null
                }
                """);
    }

    [Test]
    public async Task AcBct001CorpusIdentifiersAndCachedOraclesRemainStableAndReuseStorage()
    {
        var options = new ComparisonOptions { Documents = 4, TopK = 2, Dimensions = 4, PayloadBytes = 128, GraphVertices = 4 };
        var dataset = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(options));
        var expectedIds = new[] { "d000000000", "d000000001", "d000000002", "d000000003" };
        await Assert.That(dataset.Documents.Select(document => document.Id).ToArray())
            .IsEquivalentTo(expectedIds, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        // These fixed values are independently derived from the existing seed and event-ID formulas, not CI measurements.
        await Assert.That(dataset.Documents[0].Vector.Select(BitConverter.SingleToInt32Bits).ToArray())
            .IsEquivalentTo(new[] { 1_059_154_472, -1_088_617_754, 1_063_870_394, 1_061_666_100 },
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(BenchmarkDataset.EventId(dataset.Documents[0]))
            .IsEqualTo(Guid.Parse("7cdde467-9644-4b96-4c94-9533aaff79e2"));
        await Assert.That(ReferenceEquals(ImmutableCollectionsMarshal.AsArray(dataset.ExactNeighbors(dataset.Documents[0])),
            ImmutableCollectionsMarshal.AsArray(dataset.ExactNeighbors(dataset.Documents[0])))).IsTrue();
        await Assert.That(ReferenceEquals(ImmutableCollectionsMarshal.AsArray(dataset.Reachable(dataset.Documents[0], 2)),
            ImmutableCollectionsMarshal.AsArray(dataset.Reachable(dataset.Documents[0], 2)))).IsTrue();
        // No pre-migration corpus SHA fixture is retained in this checkout; do not invent one.
    }

    [Test]
    public async Task AcBct005CallerOwnedVectorSourceIsFrozenBeforeItBecomesContractState()
    {
        float[] source = [0.25f, -0.5f];
        var frozen = ImmutableArray.CreateRange(source);
        var document = new BenchmarkDocument(0, "d000000000", "{}", frozen);
        source[0] = 99f;
        source[1] = 99f;

        await Assert.That(document.Vector)
            .IsEquivalentTo(ImmutableArray.Create(0.25f, -0.5f), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static ComparisonReport ReportWithDefaultTargets() => new(3,
        Guid.Parse("be464719-904b-4ca8-8c14-fb48886f116b"), new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero),
        new ComparisonOptions(), "sha", "closed", "linux", "x64", 4, ".NET 10", "disk", null,
        default, ImmutableArray<ComparisonCase>.Empty);

    private static ComparisonReport ReportWithDefaultCases() => new(3,
        Guid.Parse("be464719-904b-4ca8-8c14-fb48886f116b"), new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero),
        new ComparisonOptions(), "sha", "closed", "linux", "x64", 4, ".NET 10", "disk", null,
        ImmutableArray<TargetProfile>.Empty, default);

    private static string RewriteRequiredArrays(string json, string? omit = null, string? nullValue = null)
    {
        if (omit is not null)
        {
            return json.Replace($",\"{omit}\":[]", string.Empty, StringComparison.Ordinal);
        }
        return json.Replace($"\"{nullValue}\":[]", $"\"{nullValue}\":null", StringComparison.Ordinal);
    }
}
