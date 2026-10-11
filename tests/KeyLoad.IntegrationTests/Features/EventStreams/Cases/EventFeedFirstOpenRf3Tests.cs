using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

[NotInParallel]
internal sealed class EventFeedFirstOpenRf3Tests
{
    [Test]
    public async Task ActualSixOwnerFirstOpenPinsReplaySourceRevocationRepairAndSameRootCold()
    {
        using var deadlineTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken,
            deadlineTimeout.Token);
        LocalRf3ImageTestSession? localImage = null;
        TwoRf3MembershipWave? wave = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                localImage = await LocalRf3ImageTestSession.StartIfSelectedAsync(deadline.Token).ConfigureAwait(false);
                wave = await TwoRf3MembershipWave.StartOwnedAsync(new TwoRf3MembershipWave(localImage?.Selection,
                    register: true, remote: true, query: true, probe: true, protectedDocument: true)
                { EventVectorReservedProbe = true }, deadline.Token).ConfigureAwait(false);
                await EventFeedFirstOpenRf3Flow.RunAsync(wave, deadline.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            var beforeCleanup = failures.Count;
            if (wave is not null)
            { await ServerFailureObserver.ObserveAsync(() => wave.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
            var waveJoined = wave is not null && failures.Count == beforeCleanup;
            if (localImage is not null)
            { await ServerFailureObserver.ObserveAsync(() => localImage.DisposeAsync(removeImage: waveJoined).AsTask(), failures).ConfigureAwait(false); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
