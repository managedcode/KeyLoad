namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceEvidenceParserTests
{
    private const string ModuleName = "server-resource-evidence.mjs";

    [Test]
    [Arguments("valid")]
    [Arguments("missing")]
    [Arguments("unsupported")]
    [Arguments("equivalent-targets")]
    [Arguments("separate-profiles")]
    [Arguments("complete-matched-cohort")]
    [Arguments("retained-incomplete")]
    public async Task AcScale016AcceptsBoundedNativeEvidenceAndExplicitUnavailableRows(string scenario)
        => await AssertScenarioAsync(scenario);

    [Test]
    [Arguments("wrong-worker")]
    [Arguments("wrong-job")]
    [Arguments("mismatched")]
    [Arguments("one-sample-qualified")]
    [Arguments("too-many-mounts")]
    [Arguments("invalid-cpu-set")]
    [Arguments("invalid-cpu-membership")]
    [Arguments("invalid-cpu-quota")]
    [Arguments("invalid-cpu-set-membership")]
    [Arguments("failed-workload-mismatch")]
    public async Task AcScale016RejectsMisboundOrIncomparableServerEvidence(string scenario)
        => await AssertScenarioAsync(scenario);

    private static async Task AssertScenarioAsync(string scenario)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var response = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", ScaleServerResourceNodeProgram.Source,
                IsolatedAggregateNodeProcess.Module(ModuleName), scenario], token);
        await Assert.That(response.ExitCode).IsEqualTo(0);
        await Assert.That(response.Error).IsEqualTo(string.Empty);
    }
}
