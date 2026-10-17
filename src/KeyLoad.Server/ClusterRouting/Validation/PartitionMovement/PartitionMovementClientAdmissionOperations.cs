
namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementClientAdmissionOperations
{
    internal static void RequireSenderAdmission()
        => throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMovementProtocol.Unavailable);
}
