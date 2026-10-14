using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLiveInspectionAuthorityTrial
{
    internal static async Task RunAsync(bool missingBody)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(QueueLiveInspectionAuthorityAssertions.PositiveQueue, ResourceKind.WorkQueue);
        var state = await QueueLeaseAuthoritySetup.SeedAsync(fixture, QueueLeaseAuthorityOperation.Processing,
            QueueLeaseAuthorityFault.ForeignBodyIdentity);
        var positive = await QueueLiveInspectionAuthorityAssertions.SeedPositiveAsync(fixture.Database, fixture.Store, state);
        await QueueLiveInspectionAuthorityAssertions.LeasedAsync(fixture.Database, fixture.Store, state);
        var original = QueuePendingAuthorityHistory.Capture(fixture.Store);
        var foreign = NativeSerialization.Serialize(NativeSerialization.Deserialize<MessageBody>(state.OriginalBytes)
            with
        {
            Id = QueueLeaseAuthorityProtocol.Foreign
        });
        fixture.Store.Commit((tx, _) =>
        {
            if (missingBody)
            {
                tx.Delete(state.FaultKey);
            }
            else
            {
                tx.Put(state.FaultKey, foreign);
            }
            return true;
        });
        var damaged = QueuePendingAuthorityHistory.Capture(fixture.Store);
        await QueueLiveInspectionAuthorityAssertions.OneRowAsync(original, damaged, state.FaultKey, missingBody ? null : foreign);
        await QueueLiveInspectionAuthorityAssertions.RefuseAsync(fixture.Database, fixture.Store, state, damaged);
        await QueueLiveInspectionAuthorityAssertions.RefuseAsync(fixture.Database, fixture.Store, state, damaged);
        fixture.Store.Dispose();
        QueuePendingAuthorityCut terminal;
        OperationResult processed;
        QueueLeaseAuthorityHealthyState healthy;
        using (var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            var owner = fixture.CreateConfiguredDatabase(cold);
            await QueuePendingAuthorityHistory.SameAsync(cold, damaged);
            await QueueLiveInspectionAuthorityAssertions.RefuseAsync(owner, cold, state, damaged);
            await QueueLiveInspectionAuthorityAssertions.PositiveAsync(owner, cold, state, positive);
            cold.Commit((tx, _) => { tx.Put(state.FaultKey, state.OriginalBytes); return true; });
            await QueuePendingAuthorityHistory.SameAsync(cold, original with
            {
                Position = original.Position + 2
            });
            await QueueLiveInspectionAuthorityAssertions.LeasedAsync(owner, cold, state);
            processed = owner.Apply(state.Original);
            await QueueLeaseAuthorityPostimage.RequireAsync(owner, cold, state, processed);
            await QueueLiveInspectionAuthorityAssertions.ReplayAsync(owner, cold, state, processed);
            healthy = await QueueLeaseAuthorityHealthy.RunAsync(owner, cold, state);
            await QueueLeaseAuthorityPostimage.RequireAsync(owner, cold, state, processed, healthySettled: true);
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
}
