using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class TargetInboxCapacityAuthorityContinuationFlow
{
    private const long QuotaGeneration = 4L;
    private const string QuotaDetail = "The retained target inbox capacity is exhausted.";

    internal static async Task<TargetInboxCapacityAuthorityContinuation> RunAsync(DatabaseEngine database,
        ZoneTreeStore store, TargetInboxCapacityAuthorityState state, CommitInboxRequest duplicate, CancellationToken token)
    {
        var healthyPosition = checked(database.LastApplied + 1);
        var healthy = TargetInboxSetup.Apply(database, state.Healthy, token);
        await TargetInboxCapacityAuthorityHealthy.RequireAsync(database, store, state, healthy.Get<CommitInboxResult>(), healthyPosition);
        await TargetInboxCapacityAuthorityAssertions.ReplayAsync(database, store, state.Healthy, healthy, token);
        var before = TargetInboxAssertions.Image(store, state.Original.Source.Partition, state.Original.Target.Partition);
        var reconciled = TargetInboxSetup.Apply(database, duplicate, token);
        var expected = state.OriginalResult.Get<CommitInboxResult>() with
        {
            AlreadyProcessed = true
        };
        await Assert.That(OrleansSerialization.Serialize(reconciled.Get<CommitInboxResult>()))
            .IsEquivalentTo(OrleansSerialization.Serialize(expected), CollectionOrdering.Matching);
        await Assert.That(TargetInboxAssertions.Image(store, state.Original.Source.Partition, state.Original.Target.Partition))
            .IsEquivalentTo(before, CollectionOrdering.Matching);
        await TargetInboxCapacityAuthorityAssertions.ReplayAsync(database, store, duplicate, reconciled, token);
        var quota = state.Healthy with
        {
            CommandId = Guid.NewGuid(),
            ExecutionGeneration = QuotaGeneration,
            Effects = [new PutDocument(TargetInboxUnitProtocol.Collection, TargetInboxCapacityAuthorityFixture.Refused,
                TargetInboxUnitProtocol.Payload, 0L)]
        };
        var failed = TargetInboxSetup.Apply(database, quota, token);
        await Assert.That(Errors.Code(failed.Problem).Value).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(failed.Problem?.Detail).IsEqualTo(QuotaDetail);

        await Assert.That(failed.Value).IsNull();
        await Assert.That(TargetInboxAssertions.Image(store, state.Original.Source.Partition, state.Original.Target.Partition))
            .IsEquivalentTo(before, CollectionOrdering.Matching);
        await QuotaReplayAsync(database, store, quota, failed, token);
        return new(healthy, duplicate, reconciled, quota, failed, healthyPosition, QueueWholeFlowStorage.Bytes(store), store.Position);
    }

    internal static async Task QuotaReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        CommitInboxRequest request, Result<object> expected, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        await Assert.That(OrleansSerialization.Serialize(TargetInboxSetup.Apply(database, request, token)))
            .IsEquivalentTo(OrleansSerialization.Serialize(expected), CollectionOrdering.Matching);
        await TargetInboxCapacityAuthorityAssertions.SameAsync(store, image, position);
        await Assert.That(database.GetDocument(TargetInboxUnitProtocol.Root, new(request.Target.Partition, TargetInboxUnitProtocol.Collection, TargetInboxCapacityAuthorityFixture.Refused), cancellationToken: token)).IsNull();
    }
}
