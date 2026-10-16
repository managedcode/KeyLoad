namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Exercises direct lookup and fresh revalidation policy using retained original provider metadata.</summary>
internal sealed class IsolatedKnownCurrentJobTests
{
    [Test]
    [Arguments("current")]
    [Arguments("wrong-id")]
    [Arguments("cross-run")]
    [Arguments("stale-attempt")]
    [Arguments("missing-attempt")]
    [Arguments("wrong-source")]
    [Arguments("wrong-name")]
    [Arguments("terminal")]
    public async Task KnownJobAndFreshResponseRequireTheSameCurrentAttemptBeforeContinuation(string corruption)
    {
        var fixture = Path.Combine(SessionNodeProcess.RepositoryRoot(), "tests", "KeyLoad.ComparisonTests",
            "Features", "BenchmarkComparisons", "UnitContracts", "IsolatedCurrentJobOriginal.json");
        var result = await SessionNodeProcess.RunAsync(
            ["--input-type=module", "-e", IsolatedCurrentJobNodeProgram.KnownPolicy,
                SessionNodeProcess.Module("isolated-github-api.mjs"), fixture, corruption],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Output.Trim()).IsEqualTo("accepted-known-policy");
        await Assert.That(result.Error).IsEmpty();
    }
}
