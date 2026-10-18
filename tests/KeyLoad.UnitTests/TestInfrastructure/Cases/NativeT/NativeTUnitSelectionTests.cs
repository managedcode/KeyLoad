using System.Text.Json;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class NativeTUnitSelectionTests
{
    private const string ArgumentsProperty = "args";
    private const string EnvironmentProperty = "environment";
    private const string SelectionProperty = "selected";
    private const string RevisionEnvironment = "GITHUB_SHA";
    private const string SuiteEnvironment = "KeyLoadTests__Suite";
    private const string IntrinsicEnvironment = "DOTNET_EnableHWIntrinsic";
    private const string ParallelismArgument = "--maximum-parallel-tests";
    private const string ExpectedDefaultParallelism = "50";
    private const string ExpectedExplicit20Parallelism = "20";
    private const string Explicit20Property = "explicit20";
    private const string Tuned50Property = "tuned50";
    private const string ExpectedTunedParallelism = "50";

    [Test]
    [Arguments("unit", "KeyLoad.UnitTests")]
    [Arguments("analyzers", "KeyLoad.Analyzers.Tests")]
    [Arguments("recovery", "KeyLoad.RecoveryTests")]
    [Arguments("cluster", "KeyLoad.IntegrationTests")]
    public async Task NativeEntryPreservesOriginalTUnitArgumentsAndRejectsInvalidSelections(string suite, string project)
    {
        using var selection = JsonDocument.Parse(await NativeTestSelectionProcess.ReadAsync(suite).ConfigureAwait(false));
        var selected = selection.RootElement.GetProperty(SelectionProperty);
        var args = selected.GetProperty(ArgumentsProperty).EnumerateArray().Select(value => value.GetString()).ToArray();
        var environment = selected.GetProperty(EnvironmentProperty);
        await Assert.That(args[0]).IsEqualTo("test");
        await Assert.That(args[2]).IsEqualTo("tests/" + project);
        await Assert.That(args[Array.IndexOf(args, "--output") + 1]).IsEqualTo("Detailed");
        await Assert.That(args[Array.IndexOf(args, ParallelismArgument) + 1]).IsEqualTo(ExpectedDefaultParallelism);
        await AssertParallelismAsync(selection.RootElement.GetProperty(Explicit20Property), ExpectedExplicit20Parallelism)
            .ConfigureAwait(false);
        await AssertParallelismAsync(selection.RootElement.GetProperty(Tuned50Property), ExpectedTunedParallelism)
            .ConfigureAwait(false);
        await Assert.That(args[Array.IndexOf(args, "--treenode-filter") + 1]).IsEqualTo("/*/*/ActualCase/*");
        await Assert.That(args).Contains("--report-trx");
        await Assert.That(args).Contains("--coverage");
        await Assert.That(args).DoesNotContain("build/KeyLoad.AppHost");
        await Assert.That(environment.GetProperty(RevisionEnvironment).GetString()).IsEqualTo("original-revision");
        await Assert.That(environment.TryGetProperty(SuiteEnvironment, out _)).IsFalse();
        await Assert.That(environment.TryGetProperty(IntrinsicEnvironment, out _)).IsFalse();
    }

    private static async Task AssertParallelismAsync(JsonElement selection, string expected)
    {
        var arguments = selection.GetProperty(ArgumentsProperty).EnumerateArray().Select(value => value.GetString()).ToArray();
        await Assert.That(arguments[Array.IndexOf(arguments, ParallelismArgument) + 1]).IsEqualTo(expected);
    }

}
