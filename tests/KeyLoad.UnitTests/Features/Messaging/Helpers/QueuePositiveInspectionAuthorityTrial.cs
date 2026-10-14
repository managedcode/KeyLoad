using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueuePositiveInspectionAuthorityTrial
{
    internal static async Task RunAsync(bool scheduled, bool missingBody)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(QueueLiveInspectionAuthorityAssertions.PositiveQueue, ResourceKind.WorkQueue);
        var state = await QueueLeaseAuthoritySetup.SeedAsync(fixture, QueueLeaseAuthorityOperation.Processing,
            QueueLeaseAuthorityFault.ForeignBodyIdentity);
        var positive = await QueueLiveInspectionAuthorityAssertions.SeedPositiveAsync(fixture.Database, fixture.Store, state);
        await QueueLiveInspectionAuthorityAssertions.LeasedAsync(fixture.Database, fixture.Store, state);
        var target = scheduled ? "scheduled" : "ready";
        var key = KeySpace.Partition("message-body", state.Lane.Partition,
            QueueLiveInspectionAuthorityAssertions.PositiveQueue, target);
        var exact = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        var original = QueuePendingAuthorityHistory.Capture(fixture.Store);
        var foreign = NativeSerialization.Serialize(NativeSerialization.Deserialize<MessageBody>(exact)
            with
        {
            Id = QueueLeaseAuthorityProtocol.Foreign
        });
        Damage(fixture.Store, key, foreign, missingBody);
        var damaged = QueuePendingAuthorityHistory.Capture(fixture.Store);
        await QueueLiveInspectionAuthorityAssertions.OneRowAsync(original, damaged, key, missingBody ? null : foreign);
        await QueueLiveInspectionAuthorityAssertions.PositiveRefuseAsync(fixture.Database, fixture.Store, state, positive, target, damaged);
        await QueueLiveInspectionAuthorityAssertions.PositiveRefuseAsync(fixture.Database, fixture.Store, state, positive, target, damaged);
        fixture.Store.Dispose();
        QueuePendingAuthorityCut terminal;
        OperationResult processed;
        QueueLeaseAuthorityHealthyState healthy;
        using (var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            var owner = fixture.CreateConfiguredDatabase(cold);
            await QueuePendingAuthorityHistory.SameAsync(cold, damaged);
            await QueueLiveInspectionAuthorityAssertions.PositiveRefuseAsync(owner, cold, state, positive, target, damaged);
            cold.Commit((tx, _) => { tx.Put(key, exact); return true; });
            await QueuePendingAuthorityHistory.SameAsync(cold, original with
            {
                Position = original.Position + 2
            });
            await QueueLiveInspectionAuthorityAssertions.PositiveAsync(owner, cold, state, positive);
            await QueueLiveInspectionAuthorityAssertions.LeasedAsync(owner, cold, state);
            processed = owner.Apply(state.Original);
            await QueueLeaseAuthorityPostimage.RequireAsync(owner, cold, state, processed);
            await QueueLiveInspectionAuthorityAssertions.ReplayAsync(owner, cold, state, processed);
            healthy = await QueueLeaseAuthorityHealthy.RunAsync(owner, cold, state);
            await QueueLeaseAuthorityPostimage.RequireAsync(owner, cold, state, processed, healthySettled: true);
            await QueueLiveInspectionAuthorityAssertions.PositiveAsync(owner, cold, state, positive);
            await QueueLiveInspectionAuthorityAssertions.ReplayAsync(owner, cold, state, processed);
            await QueueLeaseAuthorityHealthy.ReplayAsync(owner, cold, state, healthy);
            terminal = QueuePendingAuthorityHistory.Capture(cold);
        }
        using var second = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var reopened = fixture.CreateConfiguredDatabase(second);
        await QueuePendingAuthorityHistory.SameAsync(second, terminal);
        await QueueLiveInspectionAuthorityAssertions.PositiveAsync(reopened, second, state, positive);
        await QueueLeaseAuthorityPostimage.RequireAsync(reopened, second, state, processed, healthySettled: true);
        await QueueLiveInspectionAuthorityAssertions.ReplayAsync(reopened, second, state, processed);
        await QueueLeaseAuthorityHealthy.ReplayAsync(reopened, second, state, healthy);
        await QueuePendingAuthorityHistory.SameAsync(second, terminal);
    }

    private static void Damage(ZoneTreeStore store, byte[] key, byte[] foreign, bool missingBody)
    {
        store.Commit((tx, _) =>
        {
            if (missingBody)
            {
                tx.Delete(key);
            }
            else
            {
                tx.Put(key, foreign);
            }
            return true;
        });
    }
}
