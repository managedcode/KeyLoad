namespace KeyLoad.FunctionalTests.Authorization;

internal sealed class GenerationVisibilityTests
{
    [Test]
    public async Task CompleteSqlGraphSetsRemainTenantEpochScopedThroughPinnedRetirementRepairReplayAndCold()
    {
        using var db = new TestDatabase();
        await GenerationVisibilityFlow.RunAsync(db, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task SharedAdmissionAndCancelledCallerPreserveCanonicalRowsThenHealthyReplayAndCold()
    {
        using var db = new TestDatabase();
        await GenerationVisibilityAdmissionFlow.RunAsync(db, TestContext.Current!.Execution.CancellationToken);
    }
}
