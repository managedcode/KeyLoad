namespace KeyLoad.IntegrationTests.Messaging;

internal sealed class QueueDueDatabaseScopeClusterTests
{
    [Test]
    public Task SameNamedQueuesUseCurrentDatabaseScopeWithoutStarvingEligibleDueWorkAcrossTwoColdOwners()
        => QueueDueDatabaseScopeClusterFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
