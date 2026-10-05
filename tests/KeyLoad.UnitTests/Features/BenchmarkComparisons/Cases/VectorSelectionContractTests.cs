using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class VectorSelectionContractTests
{
    [Test]
    public async Task EveryFrozenVectorProfileBindsThroughTheTypedWorkflowSelector()
    {
        foreach (var id in VectorComparisonProfile.AllIds)
        {
            using var configuration = Selection(id);
            var actual = ComparisonWorkerSelection.Read(configuration);
            await Assert.That(actual.VectorProfile).IsEqualTo(VectorComparisonProfile.Parse(id));
            await Assert.That(actual.Profile).IsEqualTo(id);
            await Assert.That(actual.ScaledProfile).IsNull();
            await Assert.That(actual.Scenario).IsEqualTo(Scenario.VectorExact);
        }
    }

    [Test]
    public async Task EveryImmutableWorkloadOverrideAndMixedSelectorFailsBeforeNativeResources()
    {
        foreach (var setting in new[]
        {
            "Documents", "Operations", "Warmup", "Repetitions", "Concurrency", "PayloadBytes", "Seed", "Dimensions",
            "TopK", "TimeoutSeconds", "GraphVertices", "GraphFanOut", "GraphDepth", "ScaleProfile", "OpenLoopRate"
        })
        {
            using var configuration = Selection("vector-100k-exact-plain-c16");
            configuration["Benchmarks:" + setting] = setting == "ScaleProfile" ? "scaled-100k-c16" : "1";
            await Assert.That(() => ComparisonWorkerSelection.Read(configuration)).Throws<InvalidOperationException>();
        }
    }

    [Test]
    public async Task InvalidVectorSelectorScenarioAndProfileAreRejected()
    {
        foreach (var replacement in new[]
        {
            (ComparisonWorkerSelection.VectorProfileSetting, "vector-5m-exact-plain-c16"),
            (ComparisonWorkerSelection.VectorProfileSetting, ""),
            (ComparisonWorkerSelection.ScenarioSetting, nameof(Scenario.PointRead)),
            (ComparisonWorkerSelection.ProfileSetting, "intensive-1k-c16")
        })
        {
            using var configuration = Selection("vector-100k-exact-plain-c16");
            configuration[replacement.Item1] = replacement.Item2;
            await Assert.That(() => ComparisonWorkerSelection.Read(configuration)).Throws<InvalidOperationException>();
        }
    }

    private static ConfigurationManager Selection(string id)
    {
        var configuration = new ConfigurationManager();
        configuration[ComparisonWorkerSelection.TargetSetting] = "PostgreSQL + pgvector";
        configuration[ComparisonWorkerSelection.NodeCountSetting] = "1";
        configuration[ComparisonWorkerSelection.ScenarioSetting] = nameof(Scenario.VectorExact);
        configuration[ComparisonWorkerSelection.ProfileSetting] = id;
        configuration[ComparisonWorkerSelection.VectorProfileSetting] = id;
        return configuration;
    }
}
