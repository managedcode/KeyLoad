using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ComparisonParallelismSelectionTests
{
    private const string ComparisonProject = "tests/KeyLoad.ComparisonTests";
    private const string SelectedFilter = "/*/*/ActualComparison/*";
    private const string NodeIsolationMessage = "Comparison measurements and heavy load require exactly one native test at a time.";
    private const string ModuleArgument = "--input-type=module";
    private const string EvaluationArgument = "-e";
    private const string ScriptsDirectory = "scripts";
    private const string FeaturesDirectory = "Features";
    private const string SelectionSlice = "TestInfrastructure";
    private const string SelectionModule = "run-tests.mjs";
    private const string ArgumentsProperty = "args";
    private const string EnvironmentProperty = "environment";
    private const string RejectedProperty = "rejected";
    private const string RetiredProperty = "retired";
    private const string MessageProperty = "message";
    private const string ParallelismArgument = "--maximum-parallel-tests";
    private const string ConcurrencyEnvironment = "Benchmarks__Concurrency";
    private const string TargetEnvironment = "Benchmarks__Target";
    private const string KeyLoadTarget = "KeyLoad";
    private const string RevisionEnvironment = "GITHUB_SHA";
    private const string OriginalRevision = "original-revision";
    private const int RejectedSelections = 8;
    private const int RetiredSelections = 30;

    [Test]
    public async Task AcTunitEntry013NodeComparisonRejectsOverlapAndRetiredSelectorsThenPreservesHealthySelection()
    {
        var module = Path.Combine(SessionNodeProcess.RepositoryRoot(), ScriptsDirectory,
            FeaturesDirectory, SelectionSlice, SelectionModule);
        var result = await SessionNodeProcess.RunAsync(
            [ModuleArgument, EvaluationArgument, ComparisonParallelismNodeProgram.Source, module],
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
            await Assert.That(args[Array.IndexOf(args, "--minimum-expected-tests") + 1]).IsEqualTo("1");
            await Assert.That(args).Contains("--report-trx");
            var environment = selection.GetProperty(EnvironmentProperty);
            await Assert.That(environment.TryGetProperty(ConcurrencyEnvironment, out _)).IsFalse();
            await Assert.That(environment.GetProperty(TargetEnvironment).GetString()).IsEqualTo(KeyLoadTarget);
            await Assert.That(environment.GetProperty(RevisionEnvironment).GetString()).IsEqualTo(OriginalRevision);
        }
        var rejected = document.RootElement.GetProperty(RejectedProperty);
        await Assert.That(rejected.GetArrayLength()).IsEqualTo(RejectedSelections);
        foreach (var rejection in rejected.EnumerateArray().Take(3))
        {
            await Assert.That(rejection.GetProperty(MessageProperty).GetString()).IsEqualTo(NodeIsolationMessage);
        }
        await Assert.That(document.RootElement.GetProperty(RetiredProperty).GetArrayLength()).IsEqualTo(RetiredSelections);
    }

}
