using System.Text.Json;
using Aspire.Hosting;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ComparisonParallelismSelectionTests
{
    private const string ParallelismSetting = TestExecutionOptions.SectionName + ":" + nameof(TestExecutionOptions.MaximumParallelTests);
    private const string ComparisonSuite = "comparison";
    private const string ComparisonProject = "tests/KeyLoad.ComparisonTests";
    private const string TypedComparisonProject = "KeyLoad.ComparisonTests";
    private const string KeyLoadTargetName = "KeyLoad";
    private const string SelectedFilter = "/*/*/ActualComparison/*";
    private const string IsolationMessage = "Comparison measurements require exactly one native test at a time.";
    private const string ModuleArgument = "--input-type=module";
    private const string EvaluationArgument = "-e";
    private const string ScriptsDirectory = "scripts";
    private const string FeaturesDirectory = "Features";
    private const string SelectionSlice = "TestInfrastructure";
    private const string SelectionModule = "run-tests.mjs";
    private const string ArgumentsProperty = "args";
    private const string EnvironmentProperty = "environment";
    private const string RejectedProperty = "rejected";
    private const string MessageProperty = "message";
    private const string ParallelismArgument = "--maximum-parallel-tests";
    private const string ConcurrencyEnvironment = "Benchmarks__Concurrency";
    private const string ConcurrencySetting = "Benchmarks:Concurrency";
    private const string RevisionEnvironment = "GITHUB_SHA";
    private const string OriginalRevision = "original-revision";
    private const int IsolatedParallelism = 1;
    private const int RejectedSelections = 8;

    [Test]
    [Arguments("10")]
    [Arguments("500")]
    public async Task AcTunitEntry013NodeComparisonRejectsOverlapThenPreservesHealthyWorkloadSelection(string clients)
    {
        var module = Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), ScriptsDirectory,
            FeaturesDirectory, SelectionSlice, SelectionModule);
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            [ModuleArgument, EvaluationArgument, ComparisonParallelismNodeProgram.Source, module, clients],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Error).IsEmpty();
        using var document = JsonDocument.Parse(result.Output);
        foreach (var property in new[] { "selected", "explicit", "healthy" })
        {
            var selection = document.RootElement.GetProperty(property);
            var args = selection.GetProperty(ArgumentsProperty).EnumerateArray().Select(value => value.GetString()).ToArray();
            await Assert.That(args[Array.IndexOf(args, ParallelismArgument) + 1]).IsEqualTo("1");
            await Assert.That(args[2]).IsEqualTo(ComparisonProject);
            await Assert.That(args[Array.IndexOf(args, "--timeout") + 1]).IsEqualTo("140m");
            await Assert.That(args[Array.IndexOf(args, "--treenode-filter") + 1]).IsEqualTo(SelectedFilter);
            await Assert.That(args[Array.IndexOf(args, "--output") + 1]).IsEqualTo("Detailed");
            await Assert.That(args).Contains("--report-trx");
            var environment = selection.GetProperty(EnvironmentProperty);
            await Assert.That(environment.GetProperty(ConcurrencyEnvironment).GetString()).IsEqualTo(clients);
            await Assert.That(environment.GetProperty(RevisionEnvironment).GetString()).IsEqualTo(OriginalRevision);
        }
        var rejected = document.RootElement.GetProperty(RejectedProperty);
        await Assert.That(rejected.GetArrayLength()).IsEqualTo(RejectedSelections);
        foreach (var rejection in rejected.EnumerateArray().Take(3))
        {
            await Assert.That(rejection.GetProperty(MessageProperty).GetString()).IsEqualTo(IsolationMessage);
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("1")]
    public async Task AcTunitEntry013TypedComparisonSelectsSingleTestBeforeResourceComposition(string? requested)
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        ConfigureSelection(builder.Configuration);
        if (requested is not null)
        {
            builder.Configuration[ParallelismSetting] = requested;
        }
        var original = builder.Configuration.AsEnumerable().ToArray();
        var selected = TestSuiteSettings.Read(builder.Configuration)!;
        await Assert.That(selected.MaximumParallelTests).IsEqualTo(IsolatedParallelism);
        await Assert.That(selected.Project).IsEqualTo(TypedComparisonProject);
        await Assert.That(selected.Filter).IsEqualTo(SelectedFilter);
        await Assert.That(selected.ComparisonTarget).IsEqualTo(KeyLoadTargetName);
        await Assert.That(selected.Timeout).IsEqualTo(TimeSpan.FromMinutes(60));
        await Assert.That(builder.Resources.Count).IsEqualTo(0);
        await Assert.That(builder.Configuration.AsEnumerable().ToArray()).IsEquivalentTo(original);
    }

    [Test]
    [Arguments("2")]
    [Arguments("20")]
    [Arguments("50")]
    public async Task AcTunitEntry013TypedComparisonRejectsOverlapWithoutResourcesThenHealthySelectionContinues(string requested)
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        ConfigureSelection(builder.Configuration);
        builder.Configuration[ParallelismSetting] = requested;
        builder.Configuration[ConcurrencySetting] = "500";
        var original = builder.Configuration.AsEnumerable().ToArray();
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Read(builder.Configuration));
        await Assert.That(failure.Message).IsEqualTo(IsolationMessage);
        await Assert.That(builder.Resources.Count).IsEqualTo(0);
        await Assert.That(builder.Configuration.AsEnumerable().ToArray()).IsEquivalentTo(original);
        builder.Configuration[ParallelismSetting] = "1";
        var healthy = TestSuiteSettings.Read(builder.Configuration)!;
        await Assert.That(healthy.MaximumParallelTests).IsEqualTo(IsolatedParallelism);
        await Assert.That(healthy.Filter).IsEqualTo(SelectedFilter);
        await Assert.That(healthy.ComparisonTarget).IsEqualTo(KeyLoadTargetName);
        await Assert.That(builder.Configuration[ConcurrencySetting]).IsEqualTo("500");
        await Assert.That(builder.Resources.Count).IsEqualTo(0);
    }

    private static void ConfigureSelection(ConfigurationManager configuration)
    {
        configuration[TestSuiteSettings.SuiteSetting] = ComparisonSuite;
        configuration[TestSuiteSettings.FilterSetting] = SelectedFilter;
        configuration[ComparisonWorkerSelection.TargetSetting] = KeyLoadTargetName;
    }
}
