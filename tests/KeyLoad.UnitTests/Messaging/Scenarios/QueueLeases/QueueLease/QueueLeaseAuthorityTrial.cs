using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLeaseAuthorityTrial
{
    internal static async Task RunAsync(QueueLeaseAuthorityOperation kind, QueueLeaseAuthorityFault fault, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        var state = await QueueLeaseAuthoritySetup.SeedAsync(fixture, kind, fault);
        QueueLeaseAuthoritySetup.Damage(fixture.Store, state, fault);
        var damaged = QueueWholeFlowStorage.Bytes(fixture.Store);
        await QueueLeaseAuthorityRefusals.RequireAsync(fixture.Database, fixture.Store, state, fault, damaged);
        await QueueLeaseAuthorityRefusals.RequireAsync(fixture.Database, fixture.Store, state, fault, damaged);
        fixture.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(reopened);
        await QueueLeaseAuthorityRefusals.RequireAsync(database, reopened, state, fault, damaged);
        reopened.Commit((tx, _) => { tx.Put(state.FaultKey, state.OriginalBytes); return true; });
        await NativeReplayResultAssertions.Same<ReceiveResult>(database.Apply(state.Claim), state.Claimed);
        var original = database.Apply(state.Original);
        await QueueLeaseAuthorityPostimage.RequireAsync(database, reopened, state, original);
        await NativeReplayResultAssertions.Same<CommitReceipt>(database.Apply(state.Original), original);
        await OriginalClaimAsync(database, reopened, state);
        var healthy = await QueueLeaseAuthorityHealthy.RunAsync(database, reopened, state);
        var completed = QueueWholeFlowStorage.Bytes(reopened);
        reopened.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var restored = QueueWholeFlowStorage.Open(cold);
        await NativeReplayResultAssertions.Same<CommitReceipt>(restored.Apply(state.Original), original);
        await NativeReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(restored, OperationKind.Batch,
            state.Producer, state.Producer.CommandId, new DateTimeOffset(state.Original.Timestamp)), state.Produced);
        await OriginalClaimAsync(restored, cold, state);
        await QueueLeaseAuthorityHealthy.ReplayAsync(restored, cold, state, healthy);
        await QueueLeaseAuthorityPostimage.RequireAsync(restored, cold, state, original, healthySettled: true);
        await Assert.That(QueueWholeFlowStorage.Bytes(cold)).IsEquivalentTo(completed, CollectionOrdering.Matching);
    }

    private static async Task OriginalClaimAsync(DatabaseEngine database, ZoneTreeStore store, QueueLeaseAuthorityState state)
    {
        var replay = database.Apply(state.Claim);
        if (state.Kind == QueueLeaseAuthorityOperation.Renew)
        {
            await NativeReplayResultAssertions.Same<ReceiveResult>(replay, state.Claimed);
        }
        else
        {
            await Assert.That(Errors.Code(replay.Problem)).IsEqualTo(ErrorCode.StaleLease);

            await Assert.That(replay.Value).IsNull();
        }
        var retained = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            state.Lane.Partition, QueueLeaseAuthorityProtocol.Root, state.Claim.CommandId)))!;
        await NativeReplayResultAssertions.Same<ReceiveResult>(retained.Result, state.Claimed);
    }
}
