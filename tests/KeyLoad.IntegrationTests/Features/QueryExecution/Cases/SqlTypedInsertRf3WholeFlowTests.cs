namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class SqlTypedInsertRf3WholeFlowTests
{
    [Test]
    public async Task OrdinaryValuesInsertRetainsAtomicRollbackCurrentAuthorityOriginalIdentityAndTwoColdOwners()
    {
        var selection = new PostgresRf3Selection(SqlTypedInsertRf3Literal.Dialect, ObserveCancellation: true);
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(selection);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                try
                {
                    await SqlTypedInsertRf3OwnedFlow.RunAsync(fixture, selection);
                }
                catch (Exception) { selection.RetainEvidence(); throw; }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
