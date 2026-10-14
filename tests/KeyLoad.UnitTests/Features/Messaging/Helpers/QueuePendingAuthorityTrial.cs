using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueuePendingAuthorityTrial
{
    internal static async Task RunAsync(QueuePendingAuthorityFault fault)
    {
        using var fixture = new TestDatabase();
        var state = await QueuePendingAuthorityOperations.SeedAsync(fixture);
        await QueuePendingAuthorityImage.RequireAsync(fixture.Database, fixture.Store, state, QueuePendingAuthorityPhase.Pending);
        var original = QueuePendingAuthorityHistory.Capture(fixture.Store);
        QueuePendingAuthorityOperations.Damage(fixture.Store, state, fault);
        var damaged = QueuePendingAuthorityHistory.Capture(fixture.Store);
        await Assert.That(damaged.Position).IsEqualTo(original.Position + 1);
        await QueuePendingAuthorityHistory.RefuseAsync(fixture.Database, fixture.Store, state, damaged);
        await QueuePendingAuthorityHistory.RefuseAsync(fixture.Database, fixture.Store, state, damaged);
        fixture.Store.Dispose();
        QueuePendingAuthorityCut terminal;
        using (var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            var owner = fixture.CreateConfiguredDatabase(cold);
            await QueuePendingAuthorityHistory.SameAsync(cold, damaged);
            await QueuePendingAuthorityHistory.RefuseAsync(owner, cold, state, damaged);
            QueuePendingAuthorityOperations.Repair(cold, state);
            await QueuePendingAuthorityHistory.SameAsync(cold, original with
            {
                Position = original.Position + 2
            });
            await QueuePendingAuthorityImage.RequireAsync(owner, cold, state, QueuePendingAuthorityPhase.Pending);
            await QueuePendingAuthorityOperations.ParkAsync(owner, cold, state);
            await QueuePendingAuthorityImage.RequireAsync(owner, cold, state, QueuePendingAuthorityPhase.Parked);
            await QueuePendingAuthorityHistory.ReplayAsync(owner, cold, state);
            await QueuePendingAuthorityOperations.ContinueAsync(owner, cold, state);
            await QueuePendingAuthorityImage.RequireAsync(owner, cold, state, QueuePendingAuthorityPhase.Terminal);
            await QueuePendingAuthorityHistory.ReplayAsync(owner, cold, state);
            terminal = QueuePendingAuthorityHistory.Capture(cold);
        }
        using var second = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var reopened = fixture.CreateConfiguredDatabase(second);
        await QueuePendingAuthorityHistory.SameAsync(second, terminal);
        await QueuePendingAuthorityImage.RequireAsync(reopened, second, state, QueuePendingAuthorityPhase.Terminal);
        await QueuePendingAuthorityHistory.ReplayAsync(reopened, second, state);
        await QueuePendingAuthorityHistory.SameAsync(second, terminal);
    }
}
