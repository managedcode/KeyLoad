using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnPartialWriteWholeFlowTests
{
    [Test]
    public async Task ActualCompletedNativeFrameWriteCancellationLeavesNoPublicationOrAckAndHealthySameSessionPublishesLiteralPage()
    {
        var clock = new AnnPublicObservedWorkClock();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = AnnSeedTestSupport.Create(AnnPublicWholeFlow.Count, timeProvider: clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var request = NativeAnnMaintenanceTestData.Pin(database);
                await using var runtime = new NativeAnnMaintenanceTestRuntime(database, clock);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await AnnPartialWriteAssertions.PrepareAsync(database, runtime, request);
                    await AnnPartialWriteAssertions.CancelAsync(database, runtime, request, clock);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
