namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueDueDatabaseScopeRf3Tests
{
    [Test]
    public Task SameNamedQueuesUseCurrentDatabaseScopeWithoutStarvingEligibleDueWorkAcrossTwoColdOwners()
        => QueueDueDatabaseScopeRf3Flow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
