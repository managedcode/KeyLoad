using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

[NotInParallel]
internal sealed class RemoteDocumentRf3Tests
{
    private static readonly TimeSpan WaveDeadline = TimeSpan.FromMinutes(15);

    [Test]
    public async Task AcOwnerDoc001To006DestinationFreshDenialGrantPublicReadReceiptReplayAndOwnedRestart()
    {
        using var timeout = new CancellationTokenSource(WaveDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken,
            timeout.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        RemoteDocumentRf3Scenario? scenario = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartRemoteDocumentsAsync(caller.Token).ConfigureAwait(false);
            scenario = new(wave);
            await scenario.RunAsync(caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (scenario is { } calls)
        { await ServerFailureObserver.ObserveAsync(() => calls.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } nodes)
        { await ServerFailureObserver.ObserveAsync(() => nodes.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
