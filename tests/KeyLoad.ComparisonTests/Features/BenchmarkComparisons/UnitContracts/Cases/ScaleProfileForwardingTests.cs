using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class ScaleProfileForwardingTests
{
    private const string Filter = "/*/*/IsolatedNativeComparisonTests/*";
    private const string Profile = "scaled-100k-c16";

    [Test]
    public async Task AcScale014AdmitsOnlyExactComparisonHarnessAndForwardsTypedProfile()
    {
        using var configuration = ValidConfiguration();
        var settings = TestSuiteSettings.Read(configuration)!;
        await Assert.That(settings.ScaleProfile!.Id).IsEqualTo(Profile);
        await Assert.That(settings.Timeout).IsEqualTo(TimeSpan.FromMinutes(140));
        await Assert.That(settings.ComparisonTarget).IsEqualTo("KeyLoad");
    }

    [Test]
    public async Task AcScale014LeavesControlTimeoutAndProfileUnchanged()
    {
        using var configuration = new ConfigurationManager();
        configuration[TestSuiteSettings.SuiteSetting] = "comparison";
        configuration[ComparisonWorkerSelection.TargetSetting] = "KeyLoad";
        var settings = TestSuiteSettings.Read(configuration)!;
        await Assert.That(settings.ScaleProfile).IsNull();
        await Assert.That(settings.Timeout).IsEqualTo(TimeSpan.FromMinutes(60));
    }

    [Test]
    public void AcScale014RejectsMissingOrMixedHarnessAndWorkloadModes()
    {
        using var configuration = ValidConfiguration();
        configuration[TestSuiteSettings.SuiteSetting] = null;
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Read(configuration));
        configuration[TestSuiteSettings.SuiteSetting] = "unit";
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Read(configuration));
        configuration[TestSuiteSettings.SuiteSetting] = "comparison";
        configuration[TestSuiteProtocol.FilterSetting] = "/*/*/Other/*";
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Read(configuration));
        configuration[TestSuiteProtocol.FilterSetting] = Filter;
        configuration[ComparisonWorkerSelection.ScaleProfileSetting] = Profile;
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Read(configuration));
        configuration[ComparisonWorkerSelection.ScaleProfileSetting] = null;
        configuration[TestOrchestrationConfigurationKeys.BenchmarksConcurrency] = "8";
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Read(configuration));
    }

    [Test]
    public void AcScale014RejectsUnknownCaseAndEvidenceMismatchBeforeComposition()
    {
        using var configuration = ValidConfiguration();
        configuration[TestSuiteSettings.ScaleProfileSetting] = "SCALED-100K-C16";
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Read(configuration));
        configuration[TestSuiteSettings.ScaleProfileSetting] = "scaled-1m-c16";
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Read(configuration));
    }

    [Test]
    public async Task AcScale014RequestedRecognizesScaleAndRejectsEmptyCliSelector()
    {
        await Assert.That(TestSuiteSettings.Requested(["--KeyLoadTests:Suite=comparison",
            "--KeyLoadTests:ScaleProfile=scaled-100k-c16"])).IsTrue();
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Requested(
            ["--KeyLoadTests:ScaleProfile="]));
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Requested(
            ["--KeyLoadTests:ScaleProfile"]));
    }

    [Test]
    public async Task AcScale014ClearedHarnessEnvironmentIsAbsentWithoutAnOuterSuite()
    {
        using var configuration = new ConfigurationManager();
        configuration[TestSuiteSettings.ScaleProfileSetting] = string.Empty;
        await Assert.That(TestSuiteSettings.Read(configuration)).IsNull();
    }

    private static ConfigurationManager ValidConfiguration()
    {
        var configuration = new ConfigurationManager();
        configuration[TestSuiteSettings.SuiteSetting] = "comparison";
        configuration[TestSuiteProtocol.FilterSetting] = Filter;
        configuration[ComparisonWorkerSelection.TargetSetting] = "KeyLoad";
        configuration[ComparisonWorkerSelection.NodeCountSetting] = "3";
        configuration[ComparisonWorkerSelection.ScenarioSetting] = nameof(Scenario.DocumentWrite);
        configuration[ComparisonWorkerSelection.ProfileSetting] = Profile;
        configuration[TestSuiteSettings.ScaleProfileSetting] = Profile;
        return configuration;
    }
}
