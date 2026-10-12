namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class CanonicalOutcomeReadBudgetTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public Task OriginalScopedOutcomeBudgetRefusalThenExactReplayAndTwoColdHealthyOperations(bool records)
        => C1OutcomeInspectionFixture.RunOwnedAsync(fixture => CanonicalOutcomeReadBudgetFlow.RunAsync(fixture, records));
}
