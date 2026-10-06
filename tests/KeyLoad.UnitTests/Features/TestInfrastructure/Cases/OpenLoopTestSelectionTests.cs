using Aspire.Hosting;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Features.TestInfrastructure.Validation;
using KeyLoad.AppHost.Hosting;

namespace KeyLoad.UnitTests.Features.TestInfrastructure.Cases;

internal sealed class OpenLoopTestSelectionTests
{
    private const string MeasuredFilter = "/*/*/IsolatedNativeOpenLoopComparisonTests/*";
    private const string ProofFilter = "/*/*/IsolatedNativeOpenLoopCancellationTests/*";
    private const string OpenLoopRateCliOption = TestSuiteProtocol.ArgumentPrefix
        + TestSuiteSelectionValidator.OpenLoopRateSetting;
    private const string OpenLoopRateCliAssignment = OpenLoopRateCliOption + TestSuiteProtocol.ArgumentValueSeparator;
    private const string OpenLoopPlanRate = "1000";

    [Test]
    [Arguments("250")]
    [Arguments("1000")]
    [Arguments("4000")]
    public async Task AcOpenLoopSelectsMeasuredRateAndComposesTheOwnedChild(string rate)
    {
        var plan = await ComposeAsync(false, rate);
        await AssertForwardedAsync(plan, MeasuredFilter, rate);
    }

    [Test]
    [Arguments("scaled-100k-c16")]
    [Arguments("scaled-1m-c16")]
    public async Task AcOpenLoopAcceptsTheTwoActiveScaledProfileIds(string profile)
    {
        var plan = await ComposeAsync(false, "1000", profile);
        await AssertForwardedAsync(plan, MeasuredFilter, "1000", profile);
    }

    [Test]
    public async Task AcOpenLoopCancellationFilterForwardsOnlyItsKeyLoadProofSelection()
    {
        var plan = await ComposeAsync(true, "1000");
        await AssertForwardedAsync(plan, ProofFilter, "1000");
    }

    [Test]
    [Arguments(TestSuiteProtocol.OpenLoopRateSetting, "")]
    [Arguments(TestSuiteProtocol.OpenLoopRateSetting, " 250")]
    [Arguments(TestSuiteProtocol.OpenLoopRateSetting, "0250")]
    [Arguments(TestSuiteProtocol.OpenLoopRateSetting, "2500")]
    [Arguments(TestSuiteProtocol.ScaleProfileSetting, "")]
    [Arguments(TestSuiteProtocol.ScaleProfileSetting, "scaled-1m-c16")]
    [Arguments("Benchmarks:OpenLoopCancellationProof", "true")]
    [Arguments(TestSuiteProtocol.SuiteSetting, "unit")]
    [Arguments(TestSuiteProtocol.FilterSetting, "/*/*/IsolatedNativeComparisonTests/*")]
    [Arguments("Benchmarks:OpenLoopRate", "1000")]
    [Arguments("Benchmarks:ScaleProfile", "scaled-100k-c16")]
    [Arguments("Benchmarks:Operations", "100000")]
    [Arguments("Benchmarks:Profile", "intensive-1k-c16")]
    [Arguments("Benchmarks:VectorProfile", "vector-100k-hnsw-mixed-c16")]
    [Arguments("KeyLoadTests:VectorProfile", "vector-100k-hnsw-mixed-c16")]
    [Arguments("Benchmarks:Documents", "100000")]
    [Arguments("Benchmarks:Enabled", "true")]
    [Arguments("KeyLoadTests:TimeoutMinutes", "60")]
    [Arguments("KeyLoadTests:LocalRf3Image:Enabled", "true")]
    [Arguments(TestOrchestrationConfigurationKeys.BenchmarksEvidenceProfile, "scaled-1m-c16")]
    public async Task AcOpenLoopRejectsMixedConfigurationBeforeAddingAnAspireResource(string key, string value)
    {
        await AssertRejectedWithoutResourceAsync(OpenLoopAspirePlanTestBuilder.CreateBuilder(false,
            new KeyValuePair<string, string?>(key, value)));
    }

    [Test]
    public async Task AcOpenLoopRejectsInactiveFiveMillionProfileBeforeAddingAnAspireResource()
    {
        var builder = OpenLoopAspirePlanTestBuilder.CreateBuilder(false,
            new(TestSuiteProtocol.ScaleProfileSetting, "scaled-5m-c16"),
            new(TestOrchestrationConfigurationKeys.BenchmarksEvidenceProfile, "scaled-5m-c16"));
        await AssertRejectedWithoutResourceAsync(builder);
    }

    [Test]
    public async Task AcOpenLoopRejectsMissingSuiteOrScaleBeforeAddingAnAspireResource()
    {
        var missingSuite = OpenLoopAspirePlanTestBuilder.CreateBuilder();
        missingSuite.Configuration[TestSuiteProtocol.SuiteSetting] = null;
        await AssertRejectedWithoutResourceAsync(missingSuite);

        var missingScale = OpenLoopAspirePlanTestBuilder.CreateBuilder();
        missingScale.Configuration[TestSuiteProtocol.ScaleProfileSetting] = null;
        await AssertRejectedWithoutResourceAsync(missingScale);
    }

    [Test]
    [Arguments("Benchmarks:Target", "Qdrant")]
    [Arguments("Benchmarks:NodeCount", "2")]
    [Arguments("Benchmarks:Scenario", "DocumentWrite")]
    public async Task AcOpenLoopCancellationProofRejectsAnyOtherNativeIdentityBeforeResources(string key, string value)
    {
        await AssertRejectedWithoutResourceAsync(OpenLoopAspirePlanTestBuilder.CreateBuilder(true,
            new KeyValuePair<string, string?>(key, value)));
    }

    [Test]
    public async Task AcOpenLoopCliSelectorBuildsTheOwnedAspirePlan()
    {
        var arguments = new[] { OpenLoopRateCliAssignment + OpenLoopPlanRate };
        await Assert.That(TestSuiteSettings.Requested(arguments)).IsTrue();
        var builder = OpenLoopAspirePlanTestBuilder.CreateBuilderWithCommandLineArguments(arguments);
        var plan = await OpenLoopAspirePlanTestBuilder.ComposeAndReadAsync(builder);
        await AssertForwardedAsync(plan, MeasuredFilter, OpenLoopPlanRate);
    }

    [Test]
    public async Task AcOpenLoopCliRejectsAnEmptyValueBeforeAddingAnAspireResource()
    {
        var arguments = new[] { OpenLoopRateCliAssignment };
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Requested(arguments));
        var builder = OpenLoopAspirePlanTestBuilder.CreateBuilderWithCommandLineArguments(arguments,
            new KeyValuePair<string, string?>(TestSuiteSelectionValidator.OpenLoopRateSetting, null));
        await AssertRejectedWithoutResourceAsync(builder);
    }

    [Test]
    public async Task AcOpenLoopAppHostRejectsAMissingCliValueBeforeCreatingResources()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            KeyLoadAppHostApplication.RunAsync([OpenLoopRateCliOption]));
    }

    private static async Task<OpenLoopAspirePlanSnapshot> ComposeAsync(bool proof, string rate,
        string profile = "scaled-100k-c16")
    {
        var filter = proof ? ProofFilter : MeasuredFilter;
        var builder = OpenLoopAspirePlanTestBuilder.CreateBuilder(proof,
            new(TestSuiteProtocol.OpenLoopRateSetting, rate), new(TestSuiteProtocol.FilterSetting, filter),
            new(TestSuiteProtocol.ScaleProfileSetting, profile), new(TestOrchestrationConfigurationKeys.BenchmarksEvidenceProfile, profile));
        return await OpenLoopAspirePlanTestBuilder.ComposeAndReadAsync(builder);
    }

    private static async Task AssertForwardedAsync(OpenLoopAspirePlanSnapshot plan, string filter, string rate,
        string profile = "scaled-100k-c16")
    {
        var filterIndex = Array.IndexOf(plan.Arguments, "--treenode-filter");
        await Assert.That(filterIndex >= 0).IsTrue();
        await Assert.That(plan.Arguments[filterIndex + 1]).IsEqualTo(filter);
        await Assert.That(plan.Environment[TestSuiteProtocol.OpenLoopNativeRateEnvironment]).IsEqualTo(rate);
        await Assert.That(plan.Environment[TestOrchestrationConfigurationKeys.BenchmarksScaleProfileEnvironment]).IsEqualTo(profile);
        await Assert.That(plan.Environment.ContainsKey(TestOrchestrationConfigurationKeys.BenchmarksVectorProfileEnvironment)).IsFalse();
        await Assert.That(plan.Environment[TestSuiteProtocol.VectorProfileEnvironment]).IsEqualTo(string.Empty);
        await Assert.That(plan.Environment[TestSuiteProtocol.OpenLoopRateEnvironment]).IsEqualTo(string.Empty);
        await Assert.That(plan.Environment[TestSuiteProtocol.ScaleProfileEnvironment]).IsEqualTo(string.Empty);
        await Assert.That(plan.Environment[TestSuiteProtocol.SuiteEnvironment]).IsEqualTo(string.Empty);
    }

    private static async Task AssertRejectedWithoutResourceAsync(IDistributedApplicationBuilder builder)
    {
        var resourceCount = builder.Resources.Count;
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            KeyLoadAppHostApplication.AddKeyLoad(builder));
        await Assert.That(builder.Resources.Count).IsEqualTo(resourceCount);
    }
}
