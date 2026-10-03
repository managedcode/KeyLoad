using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KeyLoad.Comparisons;
using F = KeyLoad.UnitTests.Features.BenchmarkComparisons.IsolatedAggregateFields;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Controlled contract inputs only; these values are never performance evidence.</summary>
internal static class IsolatedAggregateData
{
    internal const string SourceRevision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string OtherRevision = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    internal const string DatasetHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    internal const string Image = "example.invalid/contract@sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    internal const string Repository = "managedcode/KeyLoad";
    internal const string Reference = "refs/heads/main";
    internal const string Workflow = "Benchmarks";
    internal const string Profile = "intensive-1k-c16";
    internal const string Target = "KeyLoad";
    internal const string ScenarioName = "PointRead";
    internal const string CellId = "keyload-n2-point-read";
    internal const string ContractFile = "benchmarks/KeyLoad.Comparisons/Features/BenchmarkComparisons/isolated-contract.json";
    internal const string Observation = "Controlled contract observation; no native runtime qualification.";
    private const string ControlledVersion = "controlled";
    private const string ControlledLoad = "controlled closed-loop";
    private const string Architecture = "X64";
    private const string Runtime = ".NET 10";
    internal const long RunId = 37070000000;
    internal const long JobId = 111047000000;
    internal const int Attempt = 1;
    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    internal static JsonObject Contract() => JsonNode.Parse(File.ReadAllText(Path.Combine(
        IsolatedAggregateNodeProcess.RepositoryRoot(), ContractFile)))!.AsObject();

    internal static object Cohort() => new
    {
        sourceRevision = SourceRevision,
        runId = RunId,
        attempt = Attempt,
        repository = Repository,
        @ref = Reference,
        workflow = Workflow,
        profile = Profile,
    };

    internal static object Cell() => new
    {
        id = CellId,
        target = Target,
        nodeCount = 2,
        scenario = ScenarioName,
        profile = Profile,
        family = F.Crud,
    };

    internal static JsonObject Envelope()
    {
        var options = Contract()[F.Options]!.Deserialize<ComparisonOptions>(JsonOptions)! with { Topology = ComparisonTopology.TwoNode };
        var samples = Enumerable.Range(0, options.Operations).Select(index => new OperationSample(
            index, index % options.Concurrency, index, index + 1, true, null, options.PayloadBytes, null, null)).ToImmutableArray();
        var measurement = new Measurement(options.Operations, options.Operations, 0, options.Operations / 1000d,
            1000, new(1, 1, 1), 0, null, null, null, new(0, 0, 1, 50));
        var cases = Enumerable.Range(0, options.Repetitions).Select(repetition => new ComparisonCase(
            Target, Scenario.PointRead, repetition, F.Measured, null, measurement, samples)).ToImmutableArray();
        var target = new TargetProfile(Target, ControlledVersion, Observation, Observation, Observation, Observation, Observation, Image)
        {
            Cluster = new(2, 2, Observation, [Observation]),
        };
        var report = new ComparisonReport(3, Guid.NewGuid(), TimeProvider.System.GetUtcNow(), options, DatasetHash,
            ControlledLoad, F.Ubuntu, Architecture, 4, Runtime, Observation, SourceRevision, [target], cases)
        {
            LoadGeneratorImage = Image,
            Provenance = new(RunId, Attempt, Repository, Reference, Workflow, Profile),
        };
        return JsonSerializer.SerializeToNode(new
        {
            schemaVersion = 4,
            worker = new
            {
                target = Target,
                nodeCount = 2,
                scenario = ScenarioName,
                profile = Profile,
                sourceRevision = SourceRevision,
                runId = RunId,
                attempt = Attempt,
                repository = Repository,
                @ref = Reference,
                workflow = Workflow,
                jobId = JobId,
            },
            disposition = F.Measured,
            reason = (string?)null,
            report,
        }, JsonOptions)!.AsObject();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
