using System.Text.Json;
using KeyLoad.UnitTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal sealed class WorkflowLayoutUnifiedPerformanceTests
{
    private const string NodeCounts = "nodeCounts";
    private const string Targets = "targets";
    private const string CrudScenarios = "crudScenarios";
    private const string SpecializedScenarios = "specializedScenarios";
    private const string BenchmarksFile = "benchmarks.yml";
    private const string ImageJob = "comparison-images";
    private const string PinnedImageTest = "TimeSeriesIntensivePinnedImageTests";
    private const string FactsArtifact = "name: timeseries-native-pinned-image-facts";
    private const string NativeNeeds = "needs: [comparison-plan, comparison-images]";
    private const string AggregateNeeds = "needs: [comparison-build, comparison-plan, comparison-images, comparison-preflight, comparison-crud, comparison-specialized]";
    private const string ContractPath = "benchmarks/KeyLoad.Comparisons/Features/BenchmarkComparisons/isolated-contract.json";
    private const string RegistryReadinessTest = "ImageRegistryReadinessTests";
    private const string KurrentDiscoveryTest = "IsolatedKurrentDiscoverySettingsTests";
    private const string OpenSearchQueryTest = "OpenSearchVectorQueryTests";
    private const string OpenSearchResponseTest = "OpenSearchVectorResponseTests";
    private const string SiteStartupTest = "SiteQualificationStartupTests";
    private const string UnitBuild = "dotnet build tests/KeyLoad.UnitTests";
    private const string UnitSuite = "unit";
    private const string ComparisonSuite = "comparison";
    private const string AspireCommand = "dotnet run --project src/KeyLoad.AppHost";
    private const string SuitePrefix = "--KeyLoadTests:Suite=";
    private const string ConditionalStep = "if:";
    private const string ContinueOnError = "continue-on-error:";

    [Test]
    public async Task AcBcFail006And007And010And011RegressionsAreMandatoryBeforeFanOut()
    {
        var workflow = WorkflowLayoutSource.Read(BenchmarksFile);
        var images = WorkflowLayoutSource.JobBlock(workflow, ImageJob);
        await Assert.That(images.Contains(UnitBuild, StringComparison.Ordinal)).IsTrue();
        var steps = WorkflowStepNameTests.StepBlocks(images);
        foreach (var (name, suite) in new[] { (RegistryReadinessTest, UnitSuite), (KurrentDiscoveryTest, ComparisonSuite),
            (OpenSearchQueryTest, ComparisonSuite), (OpenSearchResponseTest, ComparisonSuite), (SiteStartupTest, UnitSuite) })
        {
            await Assert.That(Count(workflow, name)).IsEqualTo(1);
            var step = steps.Single(value => value.Contains(name, StringComparison.Ordinal));
            await Assert.That(step.Contains(AspireCommand, StringComparison.Ordinal)).IsTrue();
            await Assert.That(step.Contains(SuitePrefix + suite, StringComparison.Ordinal)).IsTrue();
            await Assert.That(step.Contains(ConditionalStep, StringComparison.Ordinal)).IsFalse();
            await Assert.That(step.Contains(ContinueOnError, StringComparison.Ordinal)).IsFalse();
        }
    }

    [Test]
    public async Task AcUb001PinnedImageQualificationBelongsToCommonPreparationAndRetainsFailures()
    {
        var workflow = WorkflowLayoutSource.Read(BenchmarksFile);
        await Assert.That(WorkflowLayoutSource.JobIds(workflow)
            .Any(id => id.Contains("timeseries", StringComparison.OrdinalIgnoreCase))).IsFalse();
        var images = WorkflowLayoutSource.JobBlock(workflow, ImageJob);
        await Assert.That(images.Contains("if: github.repository == 'managedcode/KeyLoad' && github.ref == 'refs/heads/main'",
            StringComparison.Ordinal)).IsTrue();
        var steps = WorkflowStepNameTests.StepBlocks(images);
        var test = steps.Single(step => step.Contains(PinnedImageTest, StringComparison.Ordinal));
        await Assert.That(Count(workflow, PinnedImageTest)).IsEqualTo(1);
        await Assert.That(test.Contains("KEYLOAD_TIMESERIES_IMAGE_FACTS_DIRECTORY: ${{ runner.temp }}/keyload-timeseries-image-facts",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(test.Contains("dotnet run --project src/KeyLoad.AppHost", StringComparison.Ordinal)).IsTrue();
        await Assert.That(test.Contains("--KeyLoadTests:Suite=comparison", StringComparison.Ordinal)).IsTrue();
        await Assert.That(test.Contains("--KeyLoadTests:Filter=/*/*/TimeSeriesIntensivePinnedImageTests/*",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(test.Contains("--KeyLoadTests:ResultsDirectory=TestResults/timeseries-image",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(test.Contains("continue-on-error:", StringComparison.Ordinal)).IsFalse();
        await Assert.That(test.Contains("if:", StringComparison.Ordinal)).IsFalse();
        var artifact = steps.Single(step => step.Contains(FactsArtifact, StringComparison.Ordinal));
        await Assert.That(Count(workflow, FactsArtifact)).IsEqualTo(1);
        await Assert.That(artifact.Contains("if: always()", StringComparison.Ordinal)).IsTrue();
        await Assert.That(artifact.Contains("${{ runner.temp }}/keyload-timeseries-image-facts/**", StringComparison.Ordinal)).IsTrue();
        await Assert.That(artifact.Contains("if-no-files-found: error", StringComparison.Ordinal)).IsTrue();
        await AssertTimeSeriesContracts(images);
    }

    [Test]
    public async Task AcUb003And006IndependentNativeJobsJoinEveryRequiredGateBeforePublication()
    {
        var workflow = WorkflowLayoutSource.Read(BenchmarksFile);
        foreach (var jobId in new[] { "comparison-build", "comparison-plan", ImageJob })
        {
            var job = WorkflowLayoutSource.JobBlock(workflow, jobId);
            await Assert.That(job.Contains("\n    needs:", StringComparison.Ordinal)).IsFalse();
        }

        await AssertNativeMatrix(workflow, "comparison-preflight", "preflight");
        await AssertNativeMatrix(workflow, "comparison-crud", "crud");
        await AssertNativeMatrix(workflow, "comparison-specialized", "specialized");
        var aggregate = WorkflowLayoutSource.JobBlock(workflow, "comparison-aggregate");
        await Assert.That(aggregate.Contains(AggregateNeeds, StringComparison.Ordinal)).IsTrue();
        await Assert.That(aggregate.Contains("always() && !cancelled()", StringComparison.Ordinal)).IsTrue();
        await Assert.That(aggregate.Contains("needs.comparison-plan.result == 'success'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(aggregate.Contains("needs.comparison-images.result == 'success'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(aggregate.Contains("continue-on-error:", StringComparison.Ordinal)).IsFalse();
        var qualify = WorkflowLayoutSource.JobBlock(workflow, "qualify");
        await Assert.That(qualify.Contains("needs: comparison-aggregate", StringComparison.Ordinal)).IsTrue();
        await Assert.That(qualify.Contains("always() && !cancelled()", StringComparison.Ordinal)).IsTrue();
        await Assert.That(qualify.Contains("needs.comparison-aggregate.result == 'success'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(WorkflowLayoutSource.JobBlock(workflow, "deploy")
            .Contains("needs: qualify", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task AcUb002CanonicalNativeInventoryPreservesEveryTargetNodeCountAndScenario()
    {
        var root = IsolatedAggregateNodeProcess.RepositoryRoot();
        var source = await File.ReadAllTextAsync(Path.Combine(root, ContractPath),
            TestContext.Current!.Execution.CancellationToken);
        using var document = JsonDocument.Parse(source);
        var contract = document.RootElement;
        var targets = Strings(contract, Targets);
        var nodes = contract.GetProperty(NodeCounts).EnumerateArray().Select(static value => value.GetInt32()).ToArray();
        var crud = Strings(contract, CrudScenarios);
        var specialized = Strings(contract, SpecializedScenarios);
        await Assert.That(targets.SequenceEqual(new[] { "KeyLoad", "PostgreSQL + pgvector", "Qdrant", "RabbitMQ", "Redis",
            "Neo4j", "MongoDB", "OpenSearch", "KurrentDB" })).IsTrue();
        await Assert.That(nodes.SequenceEqual(new[] { 1, 2, 3 })).IsTrue();
        await Assert.That(crud.SequenceEqual(new[] { "PointRead", "DocumentWrite", "DocumentUpdate", "DocumentDelete" })).IsTrue();
        await Assert.That(specialized.SequenceEqual(new[] { "VectorExact", "QueueCycle", "GraphNeighbors", "GraphTraverse",
            "StreamAppend", "StreamRead" })).IsTrue();
        var preflights = targets.Length * nodes.Length;
        await Assert.That(preflights).IsEqualTo(27);
        await Assert.That(preflights * crud.Length).IsEqualTo(108);
        await Assert.That(preflights * specialized.Length).IsEqualTo(162);
        await Assert.That(preflights * (crud.Length + specialized.Length)).IsEqualTo(270);
        await Assert.That(preflights * crud.Length <= 256 && preflights * specialized.Length <= 256).IsTrue();
    }

    private static async Task AssertNativeMatrix(string workflow, string jobId, string output)
    {
        var job = WorkflowLayoutSource.JobBlock(workflow, jobId);
        await Assert.That(job.Contains(NativeNeeds, StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("matrix: ${{ fromJSON(needs.comparison-plan.outputs." + output + ") }}",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("runs-on: ubuntu-latest", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("fail-fast: false", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("max-parallel:", StringComparison.Ordinal)).IsFalse();
        await Assert.That(job.Contains("IsolatedNativeComparisonTests", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("continue-on-error:", StringComparison.Ordinal)).IsFalse();
    }

    private static async Task AssertTimeSeriesContracts(string images)
    {
        foreach (var test in new[] { "TimeSeriesWorkloadTests", "TimeSeriesPackageVersionTests",
            "IsolatedTimeSeriesKeyLoadResourceTests", "IsolatedTimeSeriesTimescaleResourceTests",
            "IsolatedTimeSeriesBenchmarkResourceTests" })
        {
            await Assert.That(images.Contains(test, StringComparison.Ordinal)).IsTrue();
        }
    }

    private static string[] Strings(JsonElement contract, string property) => contract.GetProperty(property)
        .EnumerateArray().Select(static value => value.GetString()!).ToArray();

    private static int Count(string source, string value) => source.Split(value, StringSplitOptions.None).Length - 1;
}
