using KeyLoad.Server;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class ComposedProjectionLineageTests
{
    [Test]
    public async Task ComposedProjectionMasksSourceFieldsRefusesForgedLineageThenRepairsAndReplaysAcrossTwoColdCuts()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(() => RunAsync(database, TestContext.Current!.Execution.CancellationToken), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunAsync(TestDatabase database, CancellationToken token)
    {
        var seed = SqlQueueReferenceJoinFixture.Seed(database);
        var repair = await ComposedProjectionLineagePolicy.RunAsync(database, token);
        await ComposedProjectionLineageCold.RunAsync(database, seed.Command, seed.Receipt, repair.Command, repair.Receipt, token);
    }
}
