using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Uses the original literal Prepare body with an independently observed live native caller.</summary>
internal static class ControlledPartitionMovementNativeRequest
{
    private const int Version = 1;
    private const int PageOrdinal = 0;
    private const int InitialOrdinal = 0;
    private const long InitialRevision = 0;

    internal static PartitionMovementTransportRequest Create(DatabaseEngine source,
        ServerRuntimeOptions runtime, PhysicalShardRecord control, PhysicalShardRecord destination,
        AtomicPartitionPlacementResolution placement, string actualCallerVoter,
        SiloAddress actualCallerAddress, long expectedRevision = InitialRevision)
    {
        var request = new PartitionMoveRequest(ControlledPartitionMovementPrepareRequest.MoveId,
            ControlledPartitionMovementCorpus.Partition, destination.PhysicalShardId,
            expectedRevision, PartitionMoveMode.Transfer);
        var body = NativeSerialization.Serialize(new PartitionMovePrepareBody(
            PhysicalShardCatalogFixture.RootPrincipalId, request));
        var envelope = new PartitionMovePeerEnvelope(Version, request.MoveId, request.Partition,
            control, placement, destination, Convert.ToHexStringLower(SHA256.HashData(body)),
            PartitionMovePeerStage.ControlPrepare, PageOrdinal,
            source.EvaluationClock.GetUtcNow() + runtime.GrainRouting.Value.RequestLifetime,
            Guid.NewGuid(), body);
        return new(ControlledPartitionMovementPrepareRequest.CommandId, envelope, null,
            actualCallerVoter, actualCallerAddress.ToParsableString(), PartitionMovementTransportAction.Apply,
            Guid.Empty, InitialOrdinal);
    }
}
