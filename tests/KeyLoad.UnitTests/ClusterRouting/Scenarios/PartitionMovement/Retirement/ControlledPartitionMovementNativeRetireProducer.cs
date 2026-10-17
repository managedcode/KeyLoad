using KeyLoad.Clustering;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeRetireProducer
{
    internal static Task<GrainOperationReply> Start(ControlledPartitionMovementNativeHosts owners,
        PartitionMoveRequest request, CancellationToken cancellationToken)
        => owners.Source.SubmitAsync(OperationKind.MovePartition, request.MoveId,
            PhysicalShardCatalogFixture.RootPrincipalId, NativeSerialization.Serialize(request), cancellationToken);

    internal static async Task CompleteAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMoveRequest request, PartitionMoveResult expected, CancellationToken cancellationToken)
    {
        var reply = await Start(owners, request, cancellationToken).ConfigureAwait(false);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var actual = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PartitionMoveResult;
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
