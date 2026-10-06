using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class VectorProfileForwardingTests
{
    private const string Profile = "vector-100k-hnsw-mixed-c16";

    [Test]
    public async Task AcVq007RoutesNativeVectorWorkloadWithItsExactProfileAndDeadline()
    {
        using var configuration = ValidConfiguration();
        var settings = TestSuiteSettings.Read(configuration)!;
        var selection = ComparisonWorkerSelection.Read(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [ComparisonWorkerSelection.TargetSetting] = settings.ComparisonTarget,
                [ComparisonWorkerSelection.NodeCountSetting] = "3",
                [ComparisonWorkerSelection.ScenarioSetting] = nameof(Scenario.VectorExact),
                [ComparisonWorkerSelection.ProfileSetting] = Profile,
                [ComparisonWorkerSelection.VectorProfileSetting] = settings.VectorProfile!.Id
            }).Build());
        await Assert.That(selection.VectorProfile!.RecordCount).IsEqualTo(100_000);
        await Assert.That(selection.VectorProfile.IndexKind).IsEqualTo(VectorIndexKind.Hnsw);
        await Assert.That(selection.VectorProfile.QueryMode).IsEqualTo(VectorQueryMode.Mixed);
        await Assert.That(settings.Timeout).IsEqualTo(TimeSpan.FromMinutes(140));
        await Assert.That(settings.ScaleProfile).IsNull();
    }

    [Test]
    [Arguments("KeyLoadTests:Suite", "unit")]
    [Arguments(TestSuiteProtocol.FilterSetting, "/*/*/ComparisonTests/*")]
    [Arguments("KeyLoadTests:TimeoutMinutes", "60")]
    [Arguments("Benchmarks:NodeCount", "4")]
    [Arguments("Benchmarks:Scenario", "PointRead")]
    [Arguments("Benchmarks:Documents", "100000")]
    [Arguments("Benchmarks:ScaleProfile", "scaled-100k-c16")]
    [Arguments("Benchmarks:VectorProfile", Profile)]
    [Arguments("Benchmarks:EvidenceProfile", "vector-1m-hnsw-mixed-c16")]
    [Arguments("KeyLoadTests:VectorProfile", "vector-5m-hnsw-mixed-c16")]
    public void AcVq007RejectsWrongCallerMixedProfilesAndChangedWorkload(string key, string value)
    {
        using var configuration = ValidConfiguration();
        configuration[key] = value;
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Read(configuration));
    }

    [Test]
    public async Task AcVq007RecognizesVectorCallerAndRejectsEmptyArgumentBeforeOrchestration()
    {
        await Assert.That(TestSuiteSettings.Requested(["--KeyLoadTests:VectorProfile=" + Profile])).IsTrue();
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Requested(["--KeyLoadTests:VectorProfile="]));
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Requested(["--KeyLoadTests:VectorProfile"]));
        using var configuration = ValidConfiguration();
        configuration[TestSuiteSettings.SuiteSetting] = null;
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Read(configuration));
    }

    private static ConfigurationManager ValidConfiguration()
    {
        var configuration = new ConfigurationManager();
        configuration[TestSuiteSettings.SuiteSetting] = "comparison";
        configuration[TestSuiteProtocol.FilterSetting] = "/*/*/IsolatedNativeComparisonTests/*";
        configuration[TestSuiteSettings.VectorProfileSetting] = Profile;
        configuration[ComparisonWorkerSelection.TargetSetting] = "Qdrant";
        configuration[ComparisonWorkerSelection.NodeCountSetting] = "3";
        configuration[ComparisonWorkerSelection.ScenarioSetting] = nameof(Scenario.VectorExact);
        configuration[ComparisonWorkerSelection.ProfileSetting] = Profile;
        return configuration;
    }
}
