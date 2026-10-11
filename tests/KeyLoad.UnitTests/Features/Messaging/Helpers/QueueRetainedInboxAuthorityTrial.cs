using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetainedInboxAuthorityTrial
{
    internal static async Task RunAsync()
    {
        using var fixture = new TestDatabase();
        var state = await QueuePendingAuthorityOperations.SeedAsync(fixture);
        await QueuePendingAuthorityImage.RequireAsync(fixture.Database, fixture.Store, state, QueuePendingAuthorityPhase.Pending);
        var history = state.Processed.Single();
        var equivalent = Operation(state, history.Request with { CommandId = Guid.NewGuid() });
        var key = QueuePendingAuthorityOperations.Key(state.Flow, QueuePendingAuthorityProtocol.Inbox,
            history.Id, history.Request.HandlerScope, history.Request.ExecutionGeneration);
        var exact = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        var original = QueuePendingAuthorityHistory.Capture(fixture.Store);
        byte[] malformed = [.. exact, 0];
        Put(fixture.Store, key, malformed);
        var damaged = QueuePendingAuthorityHistory.Capture(fixture.Store);
        await QueueRetainedInboxAuthorityAssertions.OnlySelectedRowChangedAsync(original, damaged, key, malformed);
        await QueueRetainedInboxAuthorityAssertions.RefuseAsync(fixture.Database, fixture.Store, state, equivalent, damaged);
        await QueueRetainedInboxAuthorityAssertions.RefuseAsync(fixture.Database, fixture.Store, state, equivalent, damaged);
        fixture.Store.Dispose();
        QueuePendingAuthorityCut terminal;
        ReplicatedOperation refusedGeneration;
        OperationResult failedGeneration;
        using (var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            var owner = fixture.CreateConfiguredDatabase(cold);
            await QueuePendingAuthorityHistory.SameAsync(cold, damaged);
            await QueueRetainedInboxAuthorityAssertions.OnlySelectedRowChangedAsync(original,
                QueuePendingAuthorityHistory.Capture(cold), key, malformed);
            await QueueRetainedInboxAuthorityAssertions.RefuseAsync(owner, cold, state, equivalent, damaged);
            Put(cold, key, exact);
            await QueuePendingAuthorityHistory.SameAsync(cold, original with { Position = original.Position + 2 });
            await QueuePendingAuthorityImage.RequireAsync(owner, cold, state, QueuePendingAuthorityPhase.Pending);
            await QueueRetainedInboxAuthorityAssertions.EquivalentAsync(owner, cold, state, equivalent, history, key, exact);
            refusedGeneration = Operation(state, history.Request with { CommandId = Guid.NewGuid(), ExecutionGeneration = 2 });
            failedGeneration = await QueueRetainedInboxAuthorityAssertions.NewGenerationAsync(owner, cold, state, refusedGeneration);
            await QueuePendingAuthorityOperations.ParkAsync(owner, cold, state);
            await QueuePendingAuthorityImage.RequireAsync(owner, cold, state, QueuePendingAuthorityPhase.Parked);
            await QueuePendingAuthorityOperations.ContinueAsync(owner, cold, state);
            await QueuePendingAuthorityImage.RequireAsync(owner, cold, state, QueuePendingAuthorityPhase.Terminal);
            await ReplayAsync(owner, cold, state, refusedGeneration, failedGeneration);
            terminal = QueuePendingAuthorityHistory.Capture(cold);
        }
        using var second = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var reopened = fixture.CreateConfiguredDatabase(second);
        await QueuePendingAuthorityHistory.SameAsync(second, terminal);
        await QueuePendingAuthorityImage.RequireAsync(reopened, second, state, QueuePendingAuthorityPhase.Terminal);
        await ReplayAsync(reopened, second, state, refusedGeneration, failedGeneration);
        await QueuePendingAuthorityHistory.SameAsync(second, terminal);
    }

    private static ReplicatedOperation Operation(QueuePendingAuthorityState state, ProcessingRequest request)
        => new(request.CommandId, OperationKind.Processing, QueueLifecycleTestProtocol.Administrator, state.Flow.Time,
            JsonSerializer.Serialize(request, JsonDefaults.Options));

    private static void Put(ZoneTreeStore store, byte[] key, byte[] value)
        => store.Commit((tx, _) => { tx.Put(key, value); return true; });

    private static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store, QueuePendingAuthorityState state,
        ReplicatedOperation refusal, OperationResult failed)
    {
        await QueuePendingAuthorityHistory.ReplayAsync(owner, store, state);
        await QueueRetainedInboxAuthorityAssertions.FailureReplayAsync(owner, store, state, refusal, failed);
    }
}
