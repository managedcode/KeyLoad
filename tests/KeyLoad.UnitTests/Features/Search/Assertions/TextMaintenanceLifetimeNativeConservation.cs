using KeyLoad.UnitTests.Features.ClusterReplication;
using KeyLoad.UnitTests.Features.ClusterRouting;
using K = KeyLoad.UnitTests.Features.Search.TextMaintenanceLifetimeNativeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextMaintenanceLifetimeNativeConservation
{
    internal static async Task HealthyAfterCancellationAsync(RequestCqrsCohortRuntimeFixture fixture,
        NativeLocalChildLifetimeSeeded state, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), P.Partition,
            [new PutDocument(P.Collection, K.AfterCancellationId, K.AfterCancellationJson)]);
        var index = checked(state.Leader.Journal.Log.State.LastIndex + K.NextIndex);
        var operation = state.Leader.Journal.Database.CreateNativeOperation(OperationKind.Batch,
            request.CommandId, P.Root, TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(request));
        var actual = (await state.Leader.Journal.Consensus.SubmitAsync(operation, token)).Get<CommitReceipt>();
        var expected = new CommitReceipt(request.CommandId,
            new(state.PhysicalOwner.Incarnation, P.Partition.AtomicPartitionId, index, state.PhysicalOwner.PlacementEpoch),
            [new(K.MutationKind, P.Collection, K.AfterCancellationId, K.Revision)], DurabilityProfile.ProcessDurable);
        await NativeLocalChildLifetimeAssertions.EqualAsync(expected, actual);
        var images = new Dictionary<string, (long Position, string[] Records)>();
        foreach (var voter in NativeLocalChildLifetimeProtocol.Voters)
        {
            var node = fixture.Node(voter);
            await node.Journal.Consensus.ReadBarrierAsync(token);
            var document = new DocumentResult(new(P.Partition, P.Collection, K.AfterCancellationId),
                K.Revision, K.AfterCancellationJson, false, []);
            await NativeLocalChildLifetimeAssertions.EqualAsync(document,
                node.Journal.Database.GetDocument(P.Root, document.Reference, cancellationToken: token));
            await NativeLocalChildLifetimeAssertions.RowsAsync(node, token);
            images.Add(voter, ReplicaBarrierDeadlineAssertions.Image(node));
        }
        var replay = (await state.Leader.Journal.Consensus.SubmitAsync(operation, token)).Get<CommitReceipt>();
        await NativeLocalChildLifetimeAssertions.EqualAsync(expected, replay);
        foreach (var voter in NativeLocalChildLifetimeProtocol.Voters)
        {
            var node = fixture.Node(voter);
            await node.Journal.Consensus.ReadBarrierAsync(token);
            var current = ReplicaBarrierDeadlineAssertions.Image(node);
            await Assert.That(current.Position).IsEqualTo(images[voter].Position);
            await Assert.That(current.Records.SequenceEqual(images[voter].Records)).IsTrue();
        }
    }
}
