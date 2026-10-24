namespace KeyLoad.UnitTests.RelationalStorage;

internal sealed class SqlCreateTableWholeFlowTests
{
    [Test]
    public async Task CallerStableCreateOnlySchemaConstraintRepairAndOriginalOutcomeReplayAcrossTwoColdOwners()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var principal = await SqlCreateTablePolicy.SeedAsync(fixture);
                var originalId = await SqlCreateTableRefusals.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
                var state = await SqlCreateTableHot.RunAsync(fixture, principal, originalId, TestContext.Current!.Execution.CancellationToken);
                state = await SqlCreateTablePolicy.RunAsync(fixture, state, TestContext.Current!.Execution.CancellationToken);
                await SqlCreateTableCold.RunAsync(fixture, state);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
