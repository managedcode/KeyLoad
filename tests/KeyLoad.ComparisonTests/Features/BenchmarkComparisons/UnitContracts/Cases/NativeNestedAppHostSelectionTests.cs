using Aspire.Hosting;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Features.TestInfrastructure.Validation;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.TestInfrastructure.Cases;

internal sealed class NativeNestedAppHostSelectionTests
{
    private const string NativeOpenLoopRate = "1000";
    private const string NativeCancellationProof = "true";
    private const string Target = "KeyLoad";
    private const string NativeFlowFilter = "/*/*/NativeDatabase*FlowTests/*";
    private const string TestFilterArgument = "--treenode-filter";
    private const string NativeNodeCount = "3";

    [Test]
    public async Task ClearedOpenLoopSelectorComposesNormalComparisonChildWithoutNativeRate()
    {
        var plan = await OpenLoopAspirePlanTestBuilder.ComposeAndReadAsync(CreateNormalComparisonBuilder());

        await Assert.That(plan.Environment[TestSuiteProtocol.OpenLoopRateEnvironment]).IsEqualTo(string.Empty);
        await Assert.That(plan.Environment.ContainsKey(TestSuiteProtocol.OpenLoopNativeRateEnvironment)).IsFalse();
        var filterIndex = Array.IndexOf(plan.Arguments, TestFilterArgument);
        await Assert.That(filterIndex >= 0).IsTrue();
        await Assert.That(plan.Arguments[filterIndex + 1]).IsEqualTo(NativeFlowFilter);
    }

    [Test]
    [Arguments(TestSuiteProtocol.OpenLoopRateSetting, " ")]
    [Arguments(TestOrchestrationConfigurationKeys.BenchmarksOpenLoopRate, NativeOpenLoopRate)]
    [Arguments(TestSuiteSelectionValidator.OpenLoopCancellationProofSetting, NativeCancellationProof)]
    public async Task InvalidNestedOpenLoopSelectorRejectsBeforeResourcesAndCorrectedSelectionComposes(
        string setting, string value)
    {
        var invalid = CreateNormalComparisonBuilder(new KeyValuePair<string, string?>(setting, value));
        var resourceCount = invalid.Resources.Count;
        Assert.ThrowsExactly<InvalidOperationException>(() => KeyLoadAppHostApplication.AddKeyLoad(invalid));
        await Assert.That(invalid.Resources.Count).IsEqualTo(resourceCount);

        var correctedPlan = await OpenLoopAspirePlanTestBuilder.ComposeAndReadAsync(CreateNormalComparisonBuilder());
        await Assert.That(correctedPlan.Environment[TestSuiteProtocol.OpenLoopRateEnvironment]).IsEqualTo(string.Empty);
        await Assert.That(correctedPlan.Environment.ContainsKey(TestSuiteProtocol.OpenLoopNativeRateEnvironment)).IsFalse();
    }

    [Test]
    public void ExplicitEmptyOpenLoopCliAssignmentStillRejectsBeforeResourceComposition()
    {
        string[] arguments = [TestSuiteProtocol.ArgumentPrefix + TestSuiteSelectionValidator.OpenLoopRateSetting
            + TestSuiteProtocol.ArgumentValueSeparator];
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Requested(arguments));
    }

    private static IDistributedApplicationBuilder CreateNormalComparisonBuilder(
        params KeyValuePair<string, string?>[] overrides)
    {
        var values = new List<KeyValuePair<string, string?>>
        {
            new(TestSuiteProtocol.SuiteSetting, TestSuiteProtocol.ComparisonSuite),
            new(TestSuiteProtocol.FilterSetting, NativeFlowFilter),
            new(TestSuiteProtocol.ScaleProfileSetting, null),
            new(TestSuiteProtocol.VectorProfileSetting, null),
            new(TestSuiteProtocol.OpenLoopRateSetting, string.Empty),
            new(TestSuiteProtocol.AppHostBenchmarkProfileSetting, TestSuiteProtocol.GeneralComparisonAppHostProfile),
            new(TestSuiteProtocol.BenchmarkEnabledSetting, null),
            new(ComparisonWorkerSelection.TargetSetting, Target),
            new(ComparisonWorkerSelection.NodeCountSetting, NativeNodeCount),
            new(ComparisonWorkerSelection.ScenarioSetting, nameof(Scenario.PointRead)),
            new(ComparisonWorkerSelection.ProfileSetting, IsolatedComparisonContract.Current.Profile),
            new(ComparisonWorkerSelection.ScaleProfileSetting, null),
            new(ComparisonWorkerSelection.VectorProfileSetting, null),
            new(ComparisonWorkerSelection.OpenLoopRateSetting, null),
            new(TestOrchestrationConfigurationKeys.BenchmarksOpenLoopRate, null),
            new(TestSuiteSelectionValidator.OpenLoopCancellationProofSetting, null),
            new(TestOrchestrationConfigurationKeys.BenchmarksScaleProfile, null),
            new(TestOrchestrationConfigurationKeys.BenchmarksVectorProfile, null)
        };
        values.AddRange(overrides);
        return OpenLoopAspirePlanTestBuilder.CreateBuilder(false, [.. values]);
    }
}
