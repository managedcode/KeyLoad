namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlOffsetWholeFlowTests
{
    [Test]
    public async Task AcSqlc006BoundedInitialOffsetSignedContinuationJoinRefusalsRepairAndTwoColdCuts()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase(new()
            {
                MaxResults = SqlOffsetProtocol.WindowBound
            });
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => SqlOffsetFlow.RunAsync(database), failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
