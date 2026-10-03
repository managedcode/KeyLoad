using System.Text.Json;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedSelectionTests
{
    [Test]
    public async Task AcIso002SelectionReadsOnlyOneExactTargetAndNativeNodeCount()
    {
        using var configuration = new ConfigurationManager();
        configuration[ComparisonWorkerSelection.TargetSetting] = "KeyLoad";
        configuration[ComparisonWorkerSelection.NodeCountSetting] = "2";
        configuration[ComparisonWorkerSelection.ScenarioSetting] = nameof(Scenario.DocumentUpdate);
        configuration[ComparisonWorkerSelection.ProfileSetting] = IsolatedComparisonContract.Current.Profile;
        var selection = ComparisonWorkerSelection.Read(configuration);
        await Assert.That(selection.Target).IsEqualTo("KeyLoad");
        await Assert.That(selection.NodeCount).IsEqualTo(2);
        await Assert.That(selection.Scenario).IsEqualTo(Scenario.DocumentUpdate);
        await Assert.That(selection.Options.Topology).IsEqualTo(ComparisonTopology.TwoNode);
        await Assert.That(selection.Options.Operations).IsEqualTo(10000);
        await Assert.That(selection.Options.Repetitions).IsEqualTo(5);
    }

    [Test]
    public void AcIso002UnknownTargetNodesScenarioOrProfileFailsBeforeResourceAllocation()
    {
        foreach (var selection in new[]
        {
            new ComparisonWorkerSelection("unknown", 3, Scenario.PointRead, "intensive-1k-c16"),
            new ComparisonWorkerSelection("KeyLoad", 0, Scenario.PointRead, "intensive-1k-c16"),
            new ComparisonWorkerSelection("KeyLoad", 4, Scenario.PointRead, "intensive-1k-c16"),
            new ComparisonWorkerSelection("KeyLoad", 3, (Scenario)100, "intensive-1k-c16"),
            new ComparisonWorkerSelection("KeyLoad", 3, Scenario.PointRead, "smoke")
        })
        {
            Assert.ThrowsExactly<InvalidOperationException>(selection.Validate);
        }
    }

    [Test]
    public void AcIso002MissingAndNumericScenarioConfigurationIsRejected()
    {
        using var configuration = new ConfigurationManager();
        Assert.ThrowsExactly<InvalidOperationException>(() => ComparisonWorkerSelection.Read(configuration));
        configuration[ComparisonWorkerSelection.TargetSetting] = "KeyLoad";
        configuration[ComparisonWorkerSelection.NodeCountSetting] = "3";
        configuration[ComparisonWorkerSelection.ScenarioSetting] = "0";
        configuration[ComparisonWorkerSelection.ProfileSetting] = IsolatedComparisonContract.Current.Profile;
        Assert.ThrowsExactly<InvalidOperationException>(() => ComparisonWorkerSelection.Read(configuration));
    }

    [Test]
    public async Task AcIso004NativeTwoNodeJsonAndMajorityMappingRemainExplicit()
    {
        await Assert.That(JsonSerializer.Serialize(ComparisonTopology.TwoNode, ReportWriter.JsonOptions))
            .IsEqualTo("\"TwoNode\"");
        await Assert.That(ComparisonTopologies.NodeCount(ComparisonTopology.TwoNode)).IsEqualTo(2);
        await Assert.That(ComparisonTopologies.FromNodeCount(1)).IsEqualTo(ComparisonTopology.Standalone);
        await Assert.That(ComparisonTopologies.FromNodeCount(3)).IsEqualTo(ComparisonTopology.Replicated);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ComparisonTopologies.FromNodeCount(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ComparisonTopologies.NodeCount((ComparisonTopology)100));
    }
}
