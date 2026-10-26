using System.Text.Json;
using KeyLoad.AppHost.TestInfrastructure;

namespace KeyLoad.FunctionalTests.TestInfrastructure;

internal sealed class TUnitSelectionTests
{
    private const string ArgumentsProperty = "args";
    private const string EnvironmentProperty = "environment";
    private const string SelectionProperty = "selected";
    private const string RevisionEnvironment = "GITHUB_SHA";
    private const string SuiteEnvironment = "KeyLoadTests__Suite";
    private const string IntrinsicEnvironment = "DOTNET_EnableHWIntrinsic";
    private const string PipeDirectoryEnvironment = "TESTINGPLATFORM_PIPE_DIRECTORY";
    private const string ParallelismArgument = "--maximum-parallel-tests";
    private const string FilterArgument = "--treenode-filter";
    private const string FunctionalSuite = "functional";
    private const string ClusterSuite = "cluster";
    private const string OrdinaryFunctionalFilter = "/KeyLoad.FunctionalTests/*/*/*";
    private const string OrdinaryClusterFilter = "/*/*/*/*[Category!=HeavyLoad]";
    private const string ExpectedDefaultParallelism = "50";
    private const string ExpectedExplicit20Parallelism = "20";
    private const string Explicit20Property = "explicit20";
    private const string Tuned50Property = "tuned50";
    private const string ExpectedTunedParallelism = "50";

    [Test]
    [Arguments("functional", "KeyLoad.FunctionalTests")]
    [Arguments("analyzers", "KeyLoad.AnalyzerTests")]
    [Arguments("process-recovery", "KeyLoad.ProcessRecoveryTests")]
    [Arguments("cluster", "KeyLoad.ClusterTests")]
    public async Task EntryPreservesOriginalTUnitArgumentsAndRejectsInvalidSelections(string suite, string project)
    {
        using var selection = JsonDocument.Parse(await TestSelectionProcess.ReadAsync(suite).ConfigureAwait(false));
        var selected = selection.RootElement.GetProperty(SelectionProperty);
        var args = selected.GetProperty(ArgumentsProperty).EnumerateArray().Select(value => value.GetString()).ToArray();
        var environment = selected.GetProperty(EnvironmentProperty);
        await Assert.That(Path.IsPathFullyQualified(args[0]!)).IsTrue();
        await Assert.That(args[0]).EndsWith(Path.Combine("tests", project, "bin", ProjectLaunchMetadata.Configuration, "net10.0", project + ".dll"));
        await Assert.That(args).DoesNotContain("--project");
        await Assert.That(args).DoesNotContain("--no-build");
        await Assert.That(args).DoesNotContain("--no-restore");
        await Assert.That(args[Array.IndexOf(args, "--output") + 1]).IsEqualTo("Detailed");
        await Assert.That(args[Array.IndexOf(args, ParallelismArgument) + 1]).IsEqualTo(ExpectedDefaultParallelism);
        await AssertDefaultSelectionAsync(selection.RootElement.GetProperty(Explicit20Property), ExpectedExplicit20Parallelism, suite)
            .ConfigureAwait(false);
        await AssertDefaultSelectionAsync(selection.RootElement.GetProperty(Tuned50Property), ExpectedTunedParallelism, suite)
            .ConfigureAwait(false);
        await Assert.That(args[Array.IndexOf(args, FilterArgument) + 1]).IsEqualTo("/*/*/ActualCase/*");
        await Assert.That(args).Contains("--report-trx");
        await Assert.That(args).Contains("--coverage");
        await Assert.That(args).DoesNotContain("build/KeyLoad.AppHost");
        await Assert.That(environment.GetProperty(RevisionEnvironment).GetString()).IsEqualTo("original-revision");
        await Assert.That(environment.TryGetProperty(SuiteEnvironment, out _)).IsFalse();
        await Assert.That(environment.TryGetProperty(IntrinsicEnvironment, out _)).IsFalse();
        foreach (var key in new[] { "TMPDIR", "TMP", "TEMP" })
        {
            await Assert.That(environment.GetProperty(key).GetString()).EndsWith(Path.Combine("tests", "bin", ProjectLaunchMetadata.Configuration, "ipc"));
        }
        await Assert.That(environment.GetProperty(PipeDirectoryEnvironment).GetString())
            .EndsWith(Path.Combine("tests", "bin", ProjectLaunchMetadata.Configuration, "ipc"));
    }

    private static async Task AssertDefaultSelectionAsync(JsonElement selection, string expected, string suite)
    {
        var arguments = selection.GetProperty(ArgumentsProperty).EnumerateArray().Select(value => value.GetString()).ToArray();
        await Assert.That(arguments[Array.IndexOf(arguments, ParallelismArgument) + 1]).IsEqualTo(expected);
        var filter = suite switch
        {
            FunctionalSuite => OrdinaryFunctionalFilter,
            ClusterSuite => OrdinaryClusterFilter,
            _ => null
        };
        await Assert.That(arguments.Count(argument => argument == FilterArgument)).IsEqualTo(filter is null ? 0 : 1);
        if (filter is not null)
        {
            await Assert.That(arguments[Array.IndexOf(arguments, FilterArgument) + 1]).IsEqualTo(filter);
        }
    }

}
