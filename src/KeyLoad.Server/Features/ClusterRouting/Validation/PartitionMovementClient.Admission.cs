namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementClient
{
    // This source stage exposes completed receiver capabilities, never an unchecked sender.
    // The next stage must replace this rejection with fresh quorum/current-control validation.
    private static void RequireSenderAdmission()
        => throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMovementProtocol.Unavailable);
}
