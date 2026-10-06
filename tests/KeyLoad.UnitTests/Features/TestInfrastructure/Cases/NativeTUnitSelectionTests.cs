using System.Text.Json;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class NativeTUnitSelectionTests
{
    private const string ArgumentsProperty = "args";
    private const string EnvironmentProperty = "environment";
    private const string RevisionEnvironment = "GITHUB_SHA";
    private const string SuiteEnvironment = "KeyLoadTests__Suite";
    private const string IntrinsicEnvironment = "DOTNET_EnableHWIntrinsic";

    [Test]
    [Arguments("unit", "KeyLoad.UnitTests")]
    [Arguments("unit-scalar", "KeyLoad.UnitTests")]
    [Arguments("analyzers", "KeyLoad.Analyzers.Tests")]
    [Arguments("recovery", "KeyLoad.RecoveryTests")]
    [Arguments("rf3", "KeyLoad.IntegrationTests")]
    public async Task NativeEntryPreservesOriginalTUnitArgumentsAndRejectsInvalidSelections(string suite, string project)
    {
        using var selection = JsonDocument.Parse(await NativeTestSelectionProcess.ReadAsync(suite).ConfigureAwait(false));
        var args = selection.RootElement.GetProperty(ArgumentsProperty).EnumerateArray().Select(value => value.GetString()).ToArray();
        var environment = selection.RootElement.GetProperty(EnvironmentProperty);
        await Assert.That(args[0]).IsEqualTo("test");
        await Assert.That(args[2]).IsEqualTo("tests/" + project);
        await Assert.That(args[Array.IndexOf(args, "--output") + 1]).IsEqualTo("Detailed");
        await Assert.That(args[Array.IndexOf(args, "--treenode-filter") + 1]).IsEqualTo("/*/*/ActualCase/*");
        await Assert.That(args).Contains("--report-trx");
        await Assert.That(args).Contains("--coverage");
        await Assert.That(args).DoesNotContain("src/KeyLoad.AppHost");
        await Assert.That(environment.GetProperty(RevisionEnvironment).GetString()).IsEqualTo("original-revision");
        await Assert.That(environment.TryGetProperty(SuiteEnvironment, out _)).IsFalse();
        await Assert.That(environment.TryGetProperty(IntrinsicEnvironment, out var intrinsic)).IsEqualTo(suite == "unit-scalar");
        if (suite == "unit-scalar")
        { await Assert.That(intrinsic.GetString()).IsEqualTo("0"); }
    }
}
