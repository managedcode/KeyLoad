using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

[NotInParallel]
internal sealed class PartitionWriterOrleansReplayTests
{
    [Test]
    public Task ActualOrleansTwoRf3MovementRetainedEntryRefusesChangedIdentityThenReplaysOriginalAfterColdRestart()
        => RunAsync(PartitionWriterOrleansReplayTrial.RunAsync);

    [Test]
    public Task ActualReceiverIssueAcknowledgementColdPreservesHistoricalPacketThenUnknownEffectRefusesAndAborts()
        => RunAsync(PartitionMovementHistoricalAcknowledgementTrial.RunAsync);

    private static async Task RunAsync(Func<TwoRf3MembershipWave, PartitionMovementPublicParentRf3Seed,
        CancellationToken, Task> trial)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken,
            deadline.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token).ConfigureAwait(false);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, caller.Token).ConfigureAwait(false);
            await trial(wave, seed, caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (seed is { } calls)
        {
            await ServerFailureObserver.ObserveAsync(() => calls.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (wave is { } resources)
        {
            await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
