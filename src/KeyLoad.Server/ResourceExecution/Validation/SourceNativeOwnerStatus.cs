using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class SourceNativeOwnerStatus
{
    internal static ResourceRetirementReadReplyV1 Stamp(ResourceRetirementReadReplyV1 reply,
        PartitionHost partition, ReplicaSiloDiscovery actual, int maximumResultBytes, DatabaseLimits limits)
    {
        if (reply.OwnerStatus is not { } status)
        {
            return reply;
        }
        ResourceRetirementPreparationValidation.Status(status, limits);
        var identity = partition.Database.Store.Identity;
        if (status.Cut.NodeId != identity.NodeId || status.Cut.StoreIncarnation != identity.Incarnation
            || status.Cut.ReadGeneration != identity.ReadGeneration || !actual.TransportReady
            || actual.VoterId != partition.Configuration.LocalId || actual.Incarnation != identity.Incarnation
            || !status.Reservation.Receiver.Owner.VoterIds.Contains(actual.VoterId, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementNativeProtocol.Unavailable);
        }
        var result = reply with
        {
            OwnerStatus = status with
            {
                VoterId = partition.Configuration.LocalId,
                FullSiloAddress = actual.SiloAddress
            }
        };
        if (NativeSerialization.Measure(result) > maximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
        ResourceRetirementValidation.Bound(result, limits);
        return result;
    }
}
