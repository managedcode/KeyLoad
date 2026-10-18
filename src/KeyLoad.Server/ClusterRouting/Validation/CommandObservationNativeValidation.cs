using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class CommandObservationNativeValidation
{
    internal static void Require(CommandObservationNativeRequest request, PartitionHost partition,
        NodeOptions options, SiloStorageIdentity actual, PartitionMovementMac mac)
    {
        CommandObservationNativeMac.RequireSize(request, partition);
        if (request is null || request.Version != CommandObservationNativeProtocol.Version
            || !Enum.IsDefined(request.Action) || request.Nonce == Guid.Empty || request.Caller is null
            || request.Destination != actual || !actual.TransportReady || !request.Caller.TransportReady
            || request.Caller.ClusterId != actual.ClusterId || request.Caller.Incarnation != actual.Incarnation
            || !CommandObservationNativeMac.Verify(mac, request))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, CommandObservationNativeProtocol.Invalid);
        }
        var handle = NativeCommandObservationHandleCodec.Verify(partition.Database, request.OpaqueToken);
        var configured = PhysicalOwnerConfiguredTuples.Local(options, partition.Configuration);
        if (handle.Runtime != actual
            || !PhysicalOwnerEntryValidation.SameOwner(handle.PhysicalOwner, configured.Owner)
            || !configured.Owner.VoterIds.Contains(request.Caller.VoterId, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, CommandObservationNativeProtocol.Unavailable);
        }
    }
}
