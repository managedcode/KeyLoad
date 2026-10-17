using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardUnverifiedProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardUnverifiedContinuation
{
    internal static async Task RunAsync(RequestCqrsHostedRuntimeFixture fixture, RequestCqrsCohortNativeNode[] nodes,
        RequestCqrsCohortNativeNode leader, RequestCqrsCohortNativeNode follower, ReplicatedOperation original,
        ResourceLifetimeV1 lifetime, CommitReceipt expected, ReplicaForwardUnverifiedObservation observation,
        CancellationToken token)
    {
        // Explicit NEW parent after the original indeterminate parent has ended; never a reset of that work.
        var fresh = ReplicaForwardUnverifiedFlow.CreateWork(follower, fixture.Clock, token);
        var images = nodes.Select(ReplicaBarrierDeadlineAssertions.Image).ToArray();
        var cuts = nodes.Select(node => (node.Journal.Log.State.LastIndex, node.Journal.Log.State.CommittedIndex)).ToArray();
        var replay = await ReplicaForwardUnverifiedFlow.SubmitAsync(follower, original, fresh, observation, corrupt: false, token);
        await ReplicaForwardMetadataAssertions.ImportedAsync(observation.Sample(fresh, original.Id), fresh, token);
        await ReplicaForwardMetadataAssertions.ReceiptAsync(expected, replay.Get<CommitReceipt>());
        await Assert.That(observation.Count(original.Id)).IsEqualTo(P.ReplayCount);
        await ReplicaForwardMetadataAssertions.UnchangedAsync(nodes, images, cuts);
        await ReplicaForwardUnverifiedAssertions.StateAsync(nodes, original, lifetime, expected,
            P.OriginalDocument, P.OriginalJson, P.One, token);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, fixture.Database.Partition,
            [new PutDocument(P.Collection, P.NextDocument, P.NextJson, P.NewRecord)]);
        var operation = follower.Journal.Database.CreateNativeOperation(OperationKind.Batch, id, P.Writer,
            fixture.Clock.GetUtcNow(), NativeSerialization.Serialize(request), fresh);
        var expectedIndex = checked(leader.Journal.Log.State.LastIndex + P.One);
        var healthy = await ReplicaForwardUnverifiedFlow.SubmitAsync(follower, operation, fresh, observation, corrupt: false, token);
        await ReplicaForwardMetadataAssertions.ImportedAsync(observation.Sample(fresh, id), fresh, token);
        await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
        var next = ReplicaForwardUnverifiedFlow.Receipt(id, leader, fixture.Database.Partition, expectedIndex, P.NextDocument);
        await ReplicaForwardMetadataAssertions.ReceiptAsync(next, healthy.Get<CommitReceipt>());
        await Assert.That(observation.Count(id)).IsEqualTo(P.One);
        await ReplicaForwardUnverifiedAssertions.StateAsync(nodes, operation, lifetime, next, P.NextDocument, P.NextJson, P.ReplayCount, token);
        await ReplicaForwardUnverifiedAssertions.StateAsync(nodes, original, lifetime, expected,
            P.OriginalDocument, P.OriginalJson, P.ReplayCount, token);
    }
}
