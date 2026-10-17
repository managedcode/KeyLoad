using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueuePolicyEpochRf3Tests
{
    [Test]
    public async Task ActualDeliveryReceiptsRemainPolicyFencedAfterRestoreAndHealthyEpochSurvivesBothColdPhases()
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, timeout.Token);
        LocalRf3ImageTestSession? image = null;
        TwoRf3MembershipWave? wave = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                image = await LocalRf3ImageTestSession.StartIfSelectedAsync(deadline.Token);
                wave = await TwoRf3MembershipWave.StartOwnedAsync(new TwoRf3MembershipWave(image?.Selection,
                    register: true, remote: true, query: true, probe: true, protectedDocument: true), deadline.Token);
                await QueuePolicyEpochRf3Flow.RunAsync(wave, deadline.Token);
            }, failures);
        }
        finally
        {
            var initial = failures.Count;
            if (wave is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => wave.DisposeAsync().AsTask(), failures);
            }
            if (image is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => image.DisposeAsync(removeImage: wave is not null
                && initial == failures.Count).AsTask(), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
