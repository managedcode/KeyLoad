using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Authorization;
using static KeyLoad.UnitTests.Features.QueryExecution.SchemaVisibleProjectionNativeProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SchemaVisibleProjectionNativeCold
{
    internal static async Task RunAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationCommand seed,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        for (var cold = First; cold < ColdCuts; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var rows = TeamRowPolicyAssertions.Image(owner.Store);
                var cut = owner.Store.Position;
                var journal = owner.ReadCanonicalCut();
                var applied = owner.Database.LastApplied;
                await owner.ReopenAsync();
                var checks = new List<Exception>();
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await TeamRowPolicyAssertions.UnchangedAsync(owner.Store, rows, cut);
                    await Assert.That(owner.ReadCanonicalCut()).IsEqualTo(journal);
                    await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
                }, checks);
                await ServerFailureObserver.ObserveAsync(() => SafeProjectorOperationImages.ObserveAsync(owner,
                    () => SchemaVisibleProjectionNativeRoot.WaveAsync(owner.Database,
                        new(owner.Database, UnitExecutionOptions.QueryExecution()), owner.Store, owner.Source.Partition, token)), checks);
                await ServerFailureObserver.ObserveAsync(() => SafeProjectorOperationReplay.AllAsync(owner, [seed], MixedEpoch), checks);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await TeamRowPolicyAssertions.UnchangedAsync(owner.Store, rows, cut);
                    await Assert.That(owner.ReadCanonicalCut()).IsEqualTo(journal);
                    await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
                }, checks);
                ServerFailureObserver.ThrowIfAny(checks);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
