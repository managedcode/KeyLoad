using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnPartialWriteWholeFlowTests
{
    [Test]
    public async Task ActualCompletedFrameWriteCancellationLeavesNoPublicationOrAckAndHealthySameSessionPublishesLiteralPage()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = AnnSeedTestSupport.Create(AnnPublicWholeFlow.Count);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var request = AnnMaintenanceTestData.Pin(database);
                await using var runtime = new AnnMaintenanceTestRuntime(database);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await AnnPartialWriteAssertions.PrepareAsync(database, runtime, request);
                    await AnnPartialWriteAssertions.CancelAsync(database, runtime, request);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
