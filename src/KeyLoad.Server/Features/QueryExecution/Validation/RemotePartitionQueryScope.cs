using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.QueryExecution;

internal static class RemotePartitionQueryScope
{
    internal static PartitionRef Partition(RemoteDocumentCallV1 call)
        => call.SearchLeaf?.Search?.Partition ?? call.QueryLeaf?.Plan?.Partition ?? call.Request?.Reference?.Partition
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);

    internal static bool Valid(RemoteDocumentCallV1 call)
        => call.SearchLeaf is { } search
            ? call.Request is null && call.QueryLeaf is null && search.Search is not null
                && search.Owner is not null && search.Tenant == call.Fence.Tenant
                && KeyLoad.Core.Features.ClusterRouting.Validation.PhysicalOwnerEntryValidation.SameOwner(
                    search.Owner, call.Fence.Destination.Owner)
            : (call.Request is not null) != (call.QueryLeaf is not null)
                && (call.QueryLeaf is null || call.QueryLeaf.Plan is not null
                    && call.QueryLeaf.Plan.Request is not null
                    && call.QueryLeaf.Plan.Partition == call.QueryLeaf.Plan.Request.Partition
                    && call.QueryLeaf.Owner is not null
                    && call.QueryLeaf.Tenant == call.Fence.Tenant
                    && KeyLoad.Core.Features.ClusterRouting.Validation.PhysicalOwnerEntryValidation.SameOwner(
                        call.QueryLeaf.Owner, call.Fence.Destination.Owner));
}
