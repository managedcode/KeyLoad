using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class LiveRangeSelectedCancellationAssertions
{
    internal static async Task RequireAsync(ZoneTreeStore store, bool staged, bool reverse,
        LiveRangeCancellationPhase phase)
    {
        using var selected = new CancellationTokenSource();
        if (phase == LiveRangeCancellationPhase.Before)
        {
            await selected.CancelAsync();
        }
        var callbacks = new LiveRangeCancellationCallbacks(selected, staged, reverse, phase);
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
        {
            if (staged)
            {
                store.Commit((tx, _) =>
                {
                    LiveRangeCancellationCallbacks.Stage(tx);
                    return callbacks.Visit(tx);
                });
            }
            else
            {
                store.Read(callbacks.Visit);
            }
        });
        await Assert.That(failure.CancellationToken).IsEqualTo(selected.Token);
        await Assert.That(selected.IsCancellationRequested).IsTrue();
        await callbacks.RequireCountsAsync();
    }
}
