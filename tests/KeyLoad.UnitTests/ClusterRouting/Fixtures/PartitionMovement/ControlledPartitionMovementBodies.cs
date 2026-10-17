using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Serializes the exact control body for real source fence admission, without issuing authority.</summary>
internal static class ControlledPartitionMovementBodies
{
    internal static byte[] Control(PartitionMoveControlRecord control)
        => NativeSerialization.Serialize(new PartitionMoveControlBody(
            PhysicalShardCatalogFixture.RootPrincipalId, control));
}
