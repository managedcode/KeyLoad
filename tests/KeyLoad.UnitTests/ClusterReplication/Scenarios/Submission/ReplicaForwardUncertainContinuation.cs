using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardUncertainProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardUncertainContinuation
{
    internal static async Task RunAsync(RequestCqrsHostedRuntimeFixture fixture,
        RequestCqrsCohortNativeNode[] nodes, RequestCqrsCohortNativeNode follower,
        ReplicaForwardUncertainObservation observation, ReplicatedOperation original, EntityRef reference,
        CommitReceipt expected, ReadExecutionBudget poisoned, KeyLoadException originalFailure,
        CancellationToken token)
    {
        var images = nodes.Select(ReplicaBarrierDeadlineAssertions.Image).ToArray();
        var cuts = nodes.Select(node => (node.Journal.Log.State.LastIndex, node.Journal.Log.State.CommittedIndex)).ToArray();
        var bytes = poisoned.ReadBytes;
        var records = poisoned.ConstrainedNativeRecords;
        var lookup = Assert.ThrowsExactly<KeyLoadException>(() =>
            follower.Journal.Database.ReadRetainedSubmissionOutcome(original, poisoned));
        await Assert.That(ReferenceEquals(lookup, originalFailure)).IsTrue();
        var repeated = await Assert.ThrowsAsync<KeyLoadException>(() =>
            follower.Hosted!.Coordinator.SubmitVerifiedAsync(original, token, poisoned));
        await Assert.That(ReferenceEquals(repeated, originalFailure)).IsTrue();
        await Assert.That(poisoned.ReadBytes).IsEqualTo(bytes);
        await Assert.That(poisoned.ConstrainedNativeRecords).IsEqualTo(records);
        await Assert.That(observation.CompletedForOriginal).IsEqualTo(P.One);
        await ReplicaForwardMetadataAssertions.UnchangedAsync(nodes, images, cuts);
        var fresh = ReplicaForwardUncertainFlow.Budget(follower, fixture.Clock, token);
        await Assert.That(ReferenceEquals(fresh, poisoned)).IsFalse();
        var replay = await SubmitAsync(follower, original, fresh, observation, token);
        await ReplicaForwardMetadataAssertions.ImportedAsync(observation.Sample(fresh, original.Id), fresh, token);
        await ReplicaForwardMetadataAssertions.ReceiptAsync(expected, replay.Get<CommitReceipt>());
        await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
        await Assert.That(observation.CompletedForOriginal).IsEqualTo(P.Two);
        await ReplicaForwardMetadataAssertions.UnchangedAsync(nodes, images, cuts);
        await ReplicaForwardUncertainAssertions.StateAsync(nodes, reference, expected, original, P.One, token);
        await Assert.That(fresh.HasIndeterminateMetadata).IsFalse();
        fresh.Check();
        await HealthyAsync(fixture, nodes, follower, reference, observation, token);
        await ReplicaForwardUncertainAssertions.StateAsync(nodes, reference, expected, original, P.Two, token);
    }

    private static async Task<OperationResult> SubmitAsync(RequestCqrsCohortNativeNode follower,
        ReplicatedOperation original, ReadExecutionBudget work, ReplicaForwardUncertainObservation observation,
        CancellationToken token)
    {
        observation.Begin(original.Id, work);
        try
        {
            return await follower.Hosted!.Coordinator.SubmitVerifiedAsync(original, token, work);
        }
        finally
        {
            observation.End();
        }
    }

    private static async Task HealthyAsync(RequestCqrsHostedRuntimeFixture fixture,
        RequestCqrsCohortNativeNode[] nodes, RequestCqrsCohortNativeNode follower,
        EntityRef reference, ReplicaForwardUncertainObservation observation, CancellationToken token)
    {
        var work = ReplicaForwardUncertainFlow.Budget(follower, fixture.Clock, token);
        var command = new CommandRequest(Guid.NewGuid(), reference.Partition,
            [new PutDocument(P.Collection, P.Healthy, P.Json, P.Zero)]);
        var original = follower.Journal.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId,
            P.Root, fixture.Clock.GetUtcNow(), NativeSerialization.Serialize(command), work);
        var leader = await ReplicaForwardedOriginalWorkNodes.LeaderAsync(nodes, token);
        var index = checked(leader.Journal.Log.State.LastIndex + P.One);
        var result = await SubmitAsync(follower, original, work, observation, token);
        await ReplicaForwardMetadataAssertions.ImportedAsync(observation.Sample(work, original.Id), work, token);
        var expected = new CommitReceipt(original.Id,
            new(leader.Journal.Database.Store.Identity.Incarnation, reference.Partition.AtomicPartitionId,
                index, P.InitialRevision),
            [new(P.PutKind, P.Collection, P.Healthy, P.InitialRevision)], DurabilityProfile.QuorumProcessDurable);
        await ReplicaForwardMetadataAssertions.ReceiptAsync(expected, result.Get<CommitReceipt>());
        await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
        await ReplicaForwardUncertainAssertions.StateAsync(nodes, reference with
        {
            Id = P.Healthy
        }, expected, original, P.Two, token);
        await Assert.That(work.HasIndeterminateMetadata).IsFalse();
        work.Check();
    }
}
