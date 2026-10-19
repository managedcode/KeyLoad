using SessionFields = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.BenchmarkSessionEvidenceKeys;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>The execution entry discovers shared-session cases and refuses invalid startup cohorts.</summary>
internal sealed class ComparisonHostStartupTests
{
    [Test]
    public async Task SessionDiscoveryListsSevenDistinctMeasuredTestsWithoutOpeningClients()
    {
        var result = await ComparisonHostProcess.RunAsync(["--list-tests"],
            cancellationToken: TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Stdout + result.Stderr);
        foreach (var name in SessionHostStartupSupport.RuntimeNames)
        {
            await Assert.That(result.Stdout).Contains(name);
        }
        await Assert.That(result.Stdout.Contains("ClientWorkloadProducesSuccessfulOriginalReports", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments("session-disabled")]
    [Arguments("missing-target")]
    [Arguments("invalid-nodes")]
    [Arguments("missing-session")]
    [Arguments("operation-override")]
    [Arguments("warmup-override")]
    public Task SessionRefusesIncompleteOrUnknownStartupBeforeTheMeasuredLoad(string corruption)
    {
        var settings = SessionHostStartupSupport.ValidSettings();
        var detail = SessionHostStartupSupport.Invalid;
        switch (corruption)
        {
            case "session-disabled":
                settings[SessionHostStartupSupport.Session] = "false";
                detail = "session";
                break;
            case "missing-target":
                settings.Remove(SessionHostStartupSupport.Target);
                break;
            case "invalid-nodes":
                settings[SessionHostStartupSupport.Nodes] = "2";
                detail = "InvalidOperationException: InvalidSessionSelection";
                break;
            case "missing-session":
                settings.Remove(SessionFields.SessionEnvironment);
                break;
            case "operation-override":
                settings[SessionFields.OperationsEnvironment] = "1";
                detail = "session";
                break;
            case "warmup-override":
                settings[SessionFields.WarmupEnvironment] = "1";
                detail = "session";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        return SessionHostStartupSupport.AssertRefusedAsync(settings, detail);
    }
}
