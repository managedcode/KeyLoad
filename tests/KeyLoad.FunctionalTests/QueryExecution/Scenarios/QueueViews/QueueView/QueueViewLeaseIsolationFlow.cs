using KeyLoad.FunctionalTests.DocumentStorage;
using static KeyLoad.FunctionalTests.QueryExecution.QueueViewLeaseIsolationProtocol;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueueViewLeaseIsolationFlow
{
    internal static Task RunAsync(CancellationToken token)
        => DocumentPatchAtomicityOwner.RunAsync(async owner =>
        {
            token.ThrowIfCancellationRequested();
            var state = new QueueViewLeaseIsolationState(owner, token);
            await QueueViewLeaseIsolationSeed.RunAsync(state);
            await QueueViewLeaseIsolationClaims.RunAsync(state);
            await WaveAsync(state);
            await QueueViewLeaseIsolationPolicyFlow.RunAsync(state);
            await QueueViewLeaseIsolationDelivery.RenewAsync(state);
            await WaveAsync(state);
            await QueueViewLeaseIsolationDelivery.AckAsync(state);
            await QueueViewLeaseIsolationDelivery.HealthyAsync(state);
            await WaveAsync(state);
            var original = QueueViewLeaseIsolationImages.Capture(owner);
            for (var cut = Empty; cut < ColdCuts; cut++)
            {
                token.ThrowIfCancellationRequested();
                QueueViewLeaseIsolationImages.RequireRoots(owner);
                await owner.ReopenAsync();
                QueueViewLeaseIsolationImages.RequireRoots(owner);
                await QueueViewLeaseIsolationImages.SameAsync(owner, original);
                await WaveAsync(state);
                await QueueViewLeaseIsolationImages.SameAsync(owner, original);
            }
        });

    private static async Task WaveAsync(QueueViewLeaseIsolationState state)
    {
        state.Token.ThrowIfCancellationRequested();
        await QueueViewLeaseIsolationMessages.RequireAsync(state);
        await QueueViewLeaseIsolationSource.RequireAsync(state);
        await QueueViewLeaseIsolationReads.RunAsync(state);
        await QueueViewLeaseIsolationRefusals.RunAsync(state);
        await QueueViewLeaseIsolationHistoryReplay.RunAsync(state);
    }
}
