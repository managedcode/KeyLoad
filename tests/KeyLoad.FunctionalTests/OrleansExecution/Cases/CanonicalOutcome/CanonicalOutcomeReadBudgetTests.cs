namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class CanonicalOutcomeReadBudgetTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public Task OriginalScopedOutcomeBudgetRefusalThenExactReplayAndTwoColdHealthyOperations(bool records)
        => CanonicalOutcomeFixture.RunOwnedAsync(fixture => CanonicalOutcomeReadBudgetFlow.RunAsync(fixture, records));
}
