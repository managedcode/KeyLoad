namespace KeyLoad.FunctionalTests.RelationalStorage;

internal sealed class SqlTypedInsertWholeFlowTests
{
    [Test]
    public async Task OrdinaryTypedValuesCreateOnlyRollbackCurrentAuthorityOriginalOutcomesAndTwoColdOwners()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var token = TestContext.Current!.Execution.CancellationToken;
                var state = await SqlTypedInsertHot.RunAsync(fixture, token);
                await SqlTypedInsertControls.RunAsync(fixture, state, token);
                await SqlTypedInsertParameterParity.RunAsync(fixture, state, token);
                await SqlTypedInsertResourcePolicy.RunAsync(fixture, state);
                state = await SqlTypedInsertPrincipalPolicy.RunAsync(fixture, state, token);
                await SqlTypedInsertCold.RunAsync(fixture, state, token);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
