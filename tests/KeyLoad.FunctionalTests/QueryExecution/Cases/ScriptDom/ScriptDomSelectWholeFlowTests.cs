namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class ScriptDomSelectWholeFlowTests
{
    [Test]
    public async Task AcTsql002SelectPredicateJoinWindowsRefuseRepairReplayAndTwoColdOwners()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => ScriptDomSelectFlow.RunAsync(database), failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
