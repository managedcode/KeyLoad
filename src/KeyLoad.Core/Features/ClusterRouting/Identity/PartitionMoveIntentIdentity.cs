using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Identity;

internal static class PartitionMoveIntentIdentity
{
    internal static string Digest(PartitionMoveControlRecord control)
    {
        var original = new PartitionMoveIntent(control.Version, control.MoveId, control.Partition,
            control.PrincipalId, control.PolicyEpoch, control.SourcePlacement, control.DestinationOwner,
            control.ControlPosition);
        var bytes = control.ParentCheckpointRequired
            ? NativeSerialization.Serialize(new PartitionMoveParentIntent(original, true))
            : NativeSerialization.Serialize(original);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
